﻿using OpenSpace.AI;
using OpenSpace.Animation;
using OpenSpace.Collide;
using OpenSpace.Object;
using OpenSpace.FileFormat;
using OpenSpace.FileFormat.Texture;
using OpenSpace.Input;
using OpenSpace.Text;
using OpenSpace.Visual;
using OpenSpace.Waypoints;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using OpenSpace.Object.Properties;
using System.Collections;
using OpenSpace.Loader;
using OpenSpace.Cinematics;

namespace OpenSpace {
    public class MapLoader {
        public string loadingState = "Loading";
        public string gameDataBinFolder;
        public string lvlName;

        public Material baseMaterial;
        public Material baseTransparentMaterial;
        public Material baseLightMaterial;
        public Material collideMaterial;
        public Material collideTransparentMaterial;

        public bool allowDeadPointers = false;
        public bool forceDisplayBackfaces = false;
        public bool blockyMode = false;
		public bool exportTextures = false;

        public ObjectType[][] objectTypes;
        public TextureInfo[] textures;
        public TextureInfo overlightTexture;
        public TextureInfo lightmapTexture;
        public Pointer[] persoInFix;
        public AnimationBank[] animationBanks;
        public LinkedList<Family> families;

        public InputStructure inputStruct;
        public FontStructure fontStruct;
        public string[] levels;
        public string[] languages;
        public string[] languages_loc;

        protected uint off_textures_start_fix = 0;
        protected bool hasTransit;
        public bool HasTransit {
            get { return hasTransit; }
        }

        public SuperObject transitDynamicWorld;
        public SuperObject actualWorld;
        public SuperObject dynamicWorld;
        public SuperObject inactiveDynamicWorld;
        public SuperObject fatherSector;
		public CinematicsManager cinematicsManager;

        public List<SuperObject> superObjects = new List<SuperObject>();
        public List<VisualMaterial> visualMaterials = new List<VisualMaterial>();
        public List<GameMaterial> gameMaterials = new List<GameMaterial>();
        public List<CollideMaterial> collideMaterials = new List<CollideMaterial>();
		public List<MeshObject> meshObjects = new List<MeshObject>();
        public List<LightInfo> lights = new List<LightInfo>();
        public List<Sector> sectors = new List<Sector>();
        public List<PhysicalObject> physicalObjects = new List<PhysicalObject>(); // only required for quick switching between visual & collision geometry
        public List<AIModel> aiModels = new List<AIModel>();
        public List<Behavior> behaviors = new List<Behavior>();
		public List<Macro> macros = new List<Macro>();
        public List<Perso> persos = new List<Perso>();
        public List<State> states = new List<State>();
        public List<Graph> graphs = new List<Graph>();
        public List<GraphNode> graphNodes = new List<GraphNode>();
        public List<WayPoint> waypoints = new List<WayPoint>();
        public List<KeypadEntry> keypadEntries = new List<KeypadEntry>();
        public List<MechanicsIDCard> mechanicsIDCards = new List<MechanicsIDCard>();
        public List<AnimationReference> animationReferences = new List<AnimationReference>();
        public List<AnimationMontreal> animationReferencesMontreal = new List<AnimationMontreal>();
        public List<ObjectList> objectLists = new List<ObjectList>();
        public List<ObjectList> uncategorizedObjectLists = new List<ObjectList>();
        public Dictionary<Pointer, string> strings = new Dictionary<Pointer, string>();
        public GameObject familiesRoot = null;
        //List<R3GeometricObject> parsedGO = new List<R3GeometricObject>();

        public Dictionary<ushort, SNAMemoryBlock> relocation_global = new Dictionary<ushort, SNAMemoryBlock>();
        public FileWithPointers[] files_array = new FileWithPointers[7];


        protected string[] lvlNames = new string[7];
        protected string[] lvlPaths = new string[7];
        protected string[] ptrPaths = new string[7];
        protected string[] tplPaths = new string[7];
        protected string[] cntPaths = null;
        protected CNT cnt = null;
        protected DSB gameDsb = null;
        protected DSB lvlDsb = null;
        protected string menuTPLPath;

        public Globals globals = null;
        public Settings settings = null;

        public static class Mem {
            public const int Fix = 0;
            public const int Lvl = 1;
            public const int Transit = 2;
            // 3 is also transit
            public const int VertexBuffer = 4;
            public const int FixKeyFrames = 5;
            public const int LvlKeyFrames = 6;
        }
        public int[] loadOrder = new int[] { Mem.Fix, Mem.Transit, Mem.Lvl, Mem.VertexBuffer, Mem.FixKeyFrames, Mem.LvlKeyFrames };

        public Reader livePreviewReader;

        private static MapLoader loader = null;
        public static MapLoader Loader {
            get {
                if (loader == null) {
                    if (Settings.s == null) return null;
                    if (Settings.s.engineVersion < Settings.EngineVersion.R3) {
						switch (Settings.s.platform) {
							case Settings.Platform.DC: loader = new R2DCLoader(); break;
							case Settings.Platform.PS2: loader = new R2PS2Loader(); break;
							case Settings.Platform.PS1: loader = new R2PS1Loader(); break;
							case Settings.Platform.DS:
							case Settings.Platform._3DS:
							case Settings.Platform.N64:
								loader = new R2ROMLoader(); break;
							default: loader = new R2Loader(); break;
						}
                    } else {
                        loader = new R3Loader();
                    }
                    //loader = new MapLoader();
                }
                return loader;
            }
        }
		public static void Reset() {
			loader = null;
		}

        public MapLoader() {
        }
        
        public virtual IEnumerator Load() {
            yield return null;
        }

        public void InitModdables() {
            foreach (SuperObject so in superObjects) {
                GameObject gao = so.Gao;
                if (gao != null) {
                    Moddable mod = gao.AddComponent<Moddable>();
                    mod.mat = so.matrix;
                }
            }
        }

        public void SaveModdables() {
            Writer writer = null;
            for (int i = 0; i < files_array.Length; i++) {
                if (files_array[i] != null && files_array[i].writer != null) {
                    writer = files_array[i].writer;
                    break;
                }
            }
            if (writer == null) return;
            foreach (SuperObject so in superObjects) {
                GameObject gao = so.Gao;
                if (gao != null) {
                    Moddable mod = gao.GetComponent<Moddable>();
                    if (mod != null) {
                        mod.SaveChanges(writer);
                    }
                }
            }

            foreach (Family family in families) {
                GameObject gao = family.Gao;
                if (gao != null) {
                    FamilyComponent fc = gao.GetComponent<FamilyComponent>();
                    if (fc != null) {
                        fc.SaveChanges(writer);
                    }
                }

                foreach (LightInfo light in lights) {
                    light.Write(writer);
                }
            }
        }

        public void Save() {
            try {
                for (int i = 0; i < files_array.Length; i++) {
                    if (files_array[i] != null) files_array[i].CreateWriter();
                }
                // Save changes
                SaveModdables();
            } catch (Exception e) {
                Debug.LogError(e.ToString());
            } finally {
                for (int i = 0; i < files_array.Length; i++) {
                    if (files_array[i] != null) {
                        files_array[i].Dispose();
                    }
                }
            }
        }

        #region Memory
        public void LoadMemory() {
            MemoryFile mem = (MemoryFile)files_array[0];
            if (mem == null || mem.reader == null) throw new NullReferenceException("File not initialized!");
            Reader reader = mem.reader;

            // Read object names
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["objectTypes"], mem));
            objectTypes = new ObjectType[3][];
            for (uint i = 0; i < 3; i++) {
                Pointer off_names_header = Pointer.Current(reader);
                Pointer off_names_first = Pointer.Read(reader);
                Pointer off_names_last = Pointer.Read(reader);
                uint num_names = reader.ReadUInt32();

                ReadObjectNamesTable(reader, off_names_first, num_names, i);
            }

            // Read globals
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["actualWorld"], mem));
            globals.off_actualWorld = Pointer.Read(reader);
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["dynamicWorld"], mem));
            globals.off_dynamicWorld = Pointer.Read(reader);
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["inactiveDynamicWorld"], mem));
            globals.off_inactiveDynamicWorld = Pointer.Read(reader);
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["fatherSector"], mem));
            globals.off_fatherSector = Pointer.Read(reader);
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["firstSubmapPosition"], mem));
            globals.off_firstSubMapPosition = Pointer.Read(reader);
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["always"], mem));
            globals.num_always = reader.ReadUInt32();
            globals.spawnablePersos = LinkedList<Perso>.ReadHeader(reader, Pointer.Current(reader), LinkedList.Type.Double);
            globals.off_always_reusableSO = Pointer.Read(reader); // There are (num_always) empty SuperObjects starting with this one.
            globals.off_always_reusableUnknown1 = Pointer.Read(reader); // (num_always) * 0x2c blocks
            globals.off_always_reusableUnknown2 = Pointer.Read(reader); // (num_always) * 0x4 blocks
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["families"], mem));
            families = LinkedList<Family>.ReadHeader(reader, Pointer.Current(reader), type: LinkedList.Type.Double);

            animationBanks = new AnimationBank[2];

            // Read animations
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["anim_stacks"], mem));
            if (Settings.s.engineVersion < Settings.EngineVersion.R3) {
                animationBanks[0] = AnimationBank.Read(reader, Pointer.Current(reader), 0, 1, null)[0];
                animationBanks[1] = animationBanks[0];
            } else {
                animationBanks = AnimationBank.Read(reader, Pointer.Current(reader), 0, 5, null);
            }

            // Read textures
            uint[] texMemoryChannels = new uint[1024];
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["textureMemoryChannels"], mem));
            for (int i = 0; i < 1024; i++) {
                texMemoryChannels[i] = reader.ReadUInt32();
            }
            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["textures"], mem));
            List<TextureInfo> textureInfos = new List<TextureInfo>();
            for (int i = 0; i < 1024; i++) {
                Pointer off_texture = Pointer.Read(reader);
                if (off_texture != null && texMemoryChannels[i] != 0xC0DE0005) {
                    Pointer off_current = Pointer.Goto(ref reader, off_texture);
                    TextureInfo texInfo = TextureInfo.Read(reader, off_texture);
                    //texInfo.ReadTextureFromData(reader); // Reading from GL memory doesn't seem to be possible sadly
                    // texInfo.Texture = Util.CreateDummyTexture();
                    GF gf = cnt.GetGFByTGAName(texInfo.name);
                    texInfo.Texture = gf != null ? gf.GetTexture() : null;
                    textureInfos.Add(texInfo);
                    Pointer.Goto(ref reader, off_current);
                }
            }
            textures = textureInfos.ToArray();
            
            // Parse materials list
            if (Settings.s.memoryAddresses.ContainsKey("visualMaterials") && Settings.s.memoryAddresses.ContainsKey("num_visualMaterials")) {
                Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["num_visualMaterials"], mem));
                uint num_visual_materials = reader.ReadUInt32();
                Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["visualMaterials"], mem));
                Pointer off_visualMaterials = Pointer.Read(reader);
                if (off_visualMaterials != null) {
                    Pointer.Goto(ref reader, off_visualMaterials);
                    for (uint i = 0; i < num_visual_materials; i++) {
                        Pointer off_material = Pointer.Read(reader);
                        Pointer off_current_mat = Pointer.Goto(ref reader, off_material);
                        visualMaterials.Add(VisualMaterial.Read(reader, off_material));
                        Pointer.Goto(ref reader, off_current_mat);
                    }
                }
            }

            /*if (Settings.s.memoryAddresses.ContainsKey("brightness")) {
                Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["brightness"], mem));
                float brightness = reader.ReadSingle();
                Debug.LogError("BRIGHTNESS IS " + brightness);
            }*/

            Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["inputStructure"], mem));
            inputStruct = InputStructure.Read(reader, Pointer.Current(reader));
			foreach (EntryAction ea in inputStruct.entryActions) {
				print(ea.ToString());
			}

			Pointer.Goto(ref reader, new Pointer(Settings.s.memoryAddresses["fontStructure"], mem));
            fontStruct = FontStructure.Read(reader, Pointer.Current(reader));

            // Parse actual world & always structure
            ReadFamilies(reader);
            ReadSuperObjects(reader);
            ReadAlways(reader);
            ReadCrossReferences(reader);

			// TODO: Make more generic
            if (Settings.s.game == Settings.Game.R2) {
				string path = gameDataBinFolder + "R2DC_Comports.json";
                if (!FileSystem.FileExists(path)) {
                    path = "Assets/StreamingAssets/R2DC_Comports.json"; // Offline, the json doesn't exist, so grab it from StreamingAssets
                }

				Stream stream = FileSystem.GetFileReadStream(path);
				if (stream != null) {
					ReadAndFillComportNames(stream);
				}
            }

            livePreviewReader = reader;
        }
        #endregion

        // Necessary for running StartCoroutine
        public Controller controller;
        // Defining it this way, clicking the print will go straight to the code you want
        public Action<object> print = MonoBehaviour.print;

        /*public void print(string str) {
MonoBehaviour.print(str);
}*/

        public FileWithPointers GetFileByReader(Reader reader) {
            for (int i = 0; i < files_array.Length; i++) {
                FileWithPointers file = files_array[i];
                if (file != null && reader.Equals(file.reader)) {
                    return file;
                }
            }
            return null;
        }

        public FileWithPointers GetFileByWriter(Writer writer) {
            for (int i = 0; i < files_array.Length; i++) {
                FileWithPointers file = files_array[i];
                if (file != null && writer.Equals(file.writer)) {
                    return file;
                }
            }
            return null;
        }

        public FileWithPointers InitExtraLVL(string relativePath, int id) {
            string path = gameDataBinFolder + relativePath;
            string lvlName = relativePath;
            string lvlPath = path + ".lvl";
            string ptrPath = path + ".ptr";
            if (FileSystem.FileExists(lvlPath)) {
                Array.Resize(ref files_array, files_array.Length + 1);
                LVL lvl = new LVL(lvlName, lvlPath, id);
                files_array[files_array.Length - 1] = lvl;
                if (FileSystem.FileExists(ptrPath)) {
                    lvl.ReadPTR(ptrPath);
                }
                return lvl;
            } else {
                return null;
            }
        }

		protected IEnumerator CreateCNT() {
            if (Settings.s.engineVersion < Settings.EngineVersion.R3) {
				if (Settings.s.platform != Settings.Platform.DC &&
					Settings.s.platform != Settings.Platform.PS1 &&
					Settings.s.platform != Settings.Platform.PS2) {
					List<string> cntPaths = new List<string>();
					if (gameDsb.bigfileTextures != null) cntPaths.Add(gameDataBinFolder + ConvertCase(gameDsb.bigfileTextures, Settings.CapsType.All));
					if (gameDsb.bigfileVignettes != null) cntPaths.Add(gameDataBinFolder + ConvertCase(gameDsb.bigfileVignettes, Settings.CapsType.All));
					if (cntPaths.Count > 0) {
						foreach (string path in cntPaths) {
							yield return controller.StartCoroutine(PrepareBigFile(path, 512 * 1024));
						}
						cnt = new CNT(cntPaths.ToArray());
					}
				}
            } else {
                if (Settings.s.platform == Settings.Platform.PC) {
                    if (Settings.s.game == Settings.Game.R3) {
                        cntPaths = new string[3];
                        cntPaths[0] = gameDataBinFolder + "vignette.cnt";
                        cntPaths[1] = gameDataBinFolder + "tex32_1.cnt";
                        cntPaths[2] = gameDataBinFolder + "tex32_2.cnt";
						foreach (string path in cntPaths) {
							yield return controller.StartCoroutine(PrepareBigFile(path, 512 * 1024));
						}
						cnt = new CNT(cntPaths);
                    } else if (Settings.s.game == Settings.Game.RA || Settings.s.game == Settings.Game.RM) {
                        cntPaths = new string[2];
                        cntPaths[0] = gameDataBinFolder + "vignette.cnt";
                        cntPaths[1] = gameDataBinFolder + "tex32.cnt";
						foreach (string path in cntPaths) {
							yield return controller.StartCoroutine(PrepareBigFile(path, 512 * 1024));
						}
						cnt = new CNT(cntPaths);
                    }
                }
            }
			if (cnt != null) {
				yield return controller.StartCoroutine(cnt.Init());
				cnt.SetCacheSize(2 * 1024 * 1024);
				if (exportTextures) {
					string state = loadingState;
					loadingState = "Exporting textures";
					yield return null;
					// Export all textures in cnt
					foreach (CNT.FileStruct file in cnt.fileList) {
						Util.ByteArrayToFile(gameDataBinFolder + "textures/" + file.FullName.Replace(".gf", ".png"), cnt.GetGF(file).GetTexture().EncodeToPNG());
					}
					loadingState = state;
					yield return null;
				}
				//Debug.Log("CNT init Finished!");
				yield return null;
			}
        }

        public void FillLinkedListPointers(Reader reader, Pointer lastEntry, Pointer header, uint nextOffset = 0, uint prevOffset = 4, uint headerOffset = 8) {
            Pointer current_entry = lastEntry;
            Pointer next_entry = null;
            Pointer off_current = Pointer.Current(reader);
            while (current_entry != null) {
                Pointer.Goto(ref reader, current_entry);
                current_entry.file.AddPointer(current_entry.offset + nextOffset, next_entry);
                if (header != null) {
                    current_entry.file.AddPointer(current_entry.offset + headerOffset, header);
                }
                next_entry = current_entry;
                current_entry = Pointer.GetPointerAtOffset(current_entry + prevOffset);
            }
            Pointer.Goto(ref reader, off_current);
        }

        public void ReadObjectNamesTable(Reader reader, Pointer off_names_first, uint num_names, uint index) {
            Pointer off_current = Pointer.Goto(ref reader, off_names_first);
            objectTypes[index] = new ObjectType[num_names];
            for (int j = 0; j < num_names; j++) {
                objectTypes[index][j] = new ObjectType();
                Pointer off_names_next = Pointer.Read(reader);
                Pointer off_names_prev = Pointer.Read(reader);
                Pointer off_header = Pointer.Read(reader);
                Pointer off_name = Pointer.Read(reader);
                objectTypes[index][j].unk1 = reader.ReadByte();
                objectTypes[index][j].id = reader.ReadByte();
                objectTypes[index][j].unk2 = reader.ReadUInt16();
                Pointer.Goto(ref reader, off_name);
                objectTypes[index][j].name = reader.ReadNullDelimitedString();
                if (off_names_next != null) Pointer.Goto(ref reader, off_names_next);
            }
            Pointer.Goto(ref reader, off_current);
        }

        public void ReadKeypadDefine(Reader reader, Pointer off_keypadDefine) {
            if (off_keypadDefine == null) return;
            //print("off keypad: " + off_keypadDefine);
            Pointer off_current = Pointer.Goto(ref reader, off_keypadDefine);
            bool readKeypadDefine = true;
            while (readKeypadDefine) {
                KeypadEntry entry = new KeypadEntry();
                entry.keycode = reader.ReadInt16();
                if (entry.keycode != -1) {
                    entry.unk2 = reader.ReadInt16();
                    /* Interestingly, some pointers in this list are not in the relocation table.
                     * and don't point to any key name, so they can't be read with Pointer.Read.
                     * Perhaps restoring this can help to restore debug functions... */
                    Pointer off_name = Pointer.GetPointerAtOffset(Pointer.Current(reader));
                    reader.ReadUInt32();
                    Pointer off_name2 = Pointer.GetPointerAtOffset(Pointer.Current(reader));
                    reader.ReadUInt32();
                    Pointer off_current_entry = Pointer.Current(reader);
                    if (off_name != null) {
                        Pointer.Goto(ref reader, off_name);
                        entry.name = reader.ReadNullDelimitedString();
                        //print(entry.name + " - " + entry.keycode + " - " + entry.unk2);
                    }
                    if (off_name2 != null) {
                        Pointer.Goto(ref reader, off_name2);
                        entry.name2 = reader.ReadNullDelimitedString();
                    }
                    Pointer.Goto(ref reader, off_current_entry);
                    keypadEntries.Add(entry);
                } else readKeypadDefine = false;
            }
            Pointer.Goto(ref reader, off_current);
        }

        public void ReadLevelNames(Reader reader, Pointer off_levels, uint num_levels) {
            levels = new string[num_levels];
            for (uint i = 0; i < num_levels; i++) {
                levels[i] = reader.ReadString(0x1E);
            }
        }

        public void ReadLanguages(Reader reader, Pointer off_languages, uint num_languages) {
            languages = new string[num_languages];
            languages_loc = new string[num_languages];
            for (uint i = 0; i < num_languages; i++) {
                languages[i] = reader.ReadString(0x14);
                languages_loc[i] = reader.ReadString(0x14);
            }
        }

        protected IEnumerator ReadTexturesFix(Reader reader, Pointer off_textures) {
            uint num_textureMemoryChannels = 0;
            if (Settings.s.engineVersion <= Settings.EngineVersion.R2) num_textureMemoryChannels = reader.ReadUInt32();
            uint num_textures = reader.ReadUInt32();
            print("Texture count fix: " + num_textures);
			string state = loadingState;

			textures = new TextureInfo[num_textures];
            if (num_textures > 0) {
				loadingState = "Loading fixed textures";
				yield return null;
                for (uint i = 0; i < num_textures; i++) {
                    Pointer off_texture = Pointer.Read(reader);
                    Pointer.DoAt(ref reader, off_texture, () => {
                        textures[i] = TextureInfo.Read(reader, off_texture);
                    });
                }
                if (Settings.s.platform == Settings.Platform.GC) {
                    uint num_textures_menu = reader.ReadUInt32();
                    TPL fixTPL = new TPL(tplPaths[Mem.Fix]);
                    TPL menuTPL = new TPL(menuTPLPath);
                    for (uint i = 0; i < num_textures_menu; i++) {
                        Pointer off_texture = Pointer.Read(reader);
                        TextureInfo tex = textures.Where(t => t.offset == off_texture).First();
                        tex.Texture = menuTPL.textures[i];

						if (exportTextures) {
							Util.ByteArrayToFile(gameDataBinFolder + "textures/menu/" + i + ".png", menuTPL.textures[i].EncodeToPNG());
						}
					}
                    for (int i = 0, j = 0; i < fixTPL.Count; i++, j++) {
                        while (textures[j].Texture != null) j++;
                        textures[j].Texture = fixTPL.textures[i];
						if (exportTextures) {
							Util.ByteArrayToFile(gameDataBinFolder + "textures/fix/" + i + ".png", menuTPL.textures[i].EncodeToPNG());
						}
					}
                } else if (Settings.s.platform == Settings.Platform.iOS) {
                    for (int i = 0; i < num_textures; i++) {
						loadingState = "Loading fixed textures: " + (i+1) + "/" + num_textures;
						yield return null;
						string texturePath = gameDataBinFolder + "WORLD/GRAPHICS/TEXTURES/" + textures[i].name.ToUpper().Substring(0, textures[i].name.LastIndexOf('.')) + ".GF";
                        if (FileSystem.FileExists(texturePath)) {
                            GF gf = new GF(texturePath);
                            if (gf != null) textures[i].Texture = gf.GetTexture();
                        }
                    }
                } else {
                    for (int i = 0; i < num_textures; i++) {
						loadingState = "Loading fixed textures: " + (i + 1) + "/" + num_textures;
						yield return controller.StartCoroutine(cnt.PrepareGFByTGAName(textures[i].name));
						GF gf = cnt.preparedGF;
                        if (gf != null) textures[i].Texture = gf.GetTexture();
                    }
                }
                if (Settings.s.engineVersion == Settings.EngineVersion.R3) {
                    for (uint i = 0; i < num_textures; i++) {
                        reader.ReadUInt32(); // 0 or 8.
                    }
                }
            }

			loadingState = state;
		}

		protected IEnumerator ReadTexturesLvl(Reader reader, Pointer off_textures) {
            uint num_textures_fix = (uint)textures.Length,
                num_memoryChannels = 0,
                num_textures_lvl = 0,
                num_textures_total = 0;
			string state = loadingState;
			loadingState = "Loading level textures";

			if (Settings.s.engineVersion <= Settings.EngineVersion.R2) {
                num_textures_fix = (uint)textures.Length;
                num_memoryChannels = reader.ReadUInt32();
                num_textures_lvl = reader.ReadUInt32();
                num_textures_total = num_textures_fix + num_textures_lvl;
            } else {
                num_textures_total = Settings.s.platform == Settings.Platform.GC ? reader.ReadUInt32() : 1024;
                num_textures_fix = Settings.s.platform == Settings.Platform.GC ? (uint)textures.Length : reader.ReadUInt32();
                num_textures_lvl = num_textures_total - num_textures_fix;
            }
            Array.Resize(ref textures, (int)num_textures_total);
            for (uint i = num_textures_fix; i < num_textures_total; i++) {
                Pointer off_texture = Pointer.Read(reader);
                Pointer.DoAt(ref reader, off_texture, () => {
                    textures[i] = TextureInfo.Read(reader, off_texture);
                });
            }
            if (Settings.s.engineVersion <= Settings.EngineVersion.R2) {
                uint num_texturesToCreate = reader.ReadUInt32();
                for (uint i = 0; i < num_textures_fix; i++) { // ?
                    reader.ReadUInt32(); //1
                }
                uint currentMemoryChannel = reader.ReadUInt32();
            }
            if (Settings.s.platform == Settings.Platform.GC) {
                // Load textures from TPL
                TPL lvlTPL = new TPL(tplPaths[Mem.Lvl]);
                TPL transitTPL = hasTransit ? new TPL(tplPaths[Mem.Transit]) : null;
                print("Lvl TPL Texture count: " + lvlTPL.Count);
                if (hasTransit) print("Transit TPL Texture count: " + transitTPL.Count);
                int transitTexturesSeen = 0;
                for (uint i = num_textures_fix; i < num_textures_total; i++) {
                    uint file_texture = reader.ReadUInt32();
                    if (hasTransit && file_texture == 6) {
                        textures[i].Texture = transitTPL.textures[transitTexturesSeen++];
                    } else {
                        textures[i].Texture = lvlTPL.textures[i - num_textures_fix - transitTexturesSeen];
                    }
                }
				if (exportTextures) {
					if (transitTPL != null) {
						for(int i = 0; i < transitTPL.textures.Length; i++) {
							Util.ByteArrayToFile(gameDataBinFolder + "textures/" + Path.GetFileNameWithoutExtension(transitTPL.path) + "/" + i + ".png", transitTPL.textures[i].EncodeToPNG());
						}
					}
					if (lvlTPL != null) {
						for (int i = 0; i < lvlTPL.textures.Length; i++) {
							Util.ByteArrayToFile(gameDataBinFolder + "textures/" + Path.GetFileNameWithoutExtension(lvlTPL.path) + "/" + i + ".png", lvlTPL.textures[i].EncodeToPNG());
						}
					}
				}
			} else if (Settings.s.platform == Settings.Platform.iOS) {
                // Load textures from separate GF files
                for (uint i = num_textures_fix; i < num_textures_total; i++) {
					if (textures[i] == null) continue;
					loadingState = "Loading level textures: " + (i - num_textures_fix + 1) + "/" + (num_textures_total - num_textures_fix);
					yield return null;
					string texturePath = gameDataBinFolder + "WORLD/GRAPHICS/TEXTURES/" + textures[i].name.ToUpper().Substring(0, textures[i].name.LastIndexOf('.')) + ".GF";
					yield return controller.StartCoroutine(PrepareFile(texturePath));
					if (FileSystem.FileExists(texturePath)) {
                        GF gf = new GF(texturePath);
                        if (gf != null) textures[i].Texture = gf.GetTexture();
                    }
                }
            } else {
                // Load textures from CNT
                int transitTexturesSeen = 0;
				int num_textures_level_real = 0;
				Pointer off_current = Pointer.Current(reader);
				for (uint i = num_textures_fix; i < num_textures_total; i++) {
					uint file_texture = Settings.s.engineVersion == Settings.EngineVersion.R3 ? reader.ReadUInt32() : 0;
					if (file_texture == 0xC0DE2005 || textures[i] == null) continue; // texture is undefined
					num_textures_level_real++;
				}
				Pointer.Goto(ref reader, off_current);
				int current_texture = 0;
				for (uint i = num_textures_fix; i < num_textures_total; i++) {
                    uint file_texture = Settings.s.engineVersion == Settings.EngineVersion.R3 ? reader.ReadUInt32() : 0;
                    if (file_texture == 0xC0DE2005 || textures[i] == null) continue; // texture is undefined
					current_texture++;
					loadingState = "Loading level textures: " + current_texture + "/" + (num_textures_level_real);
					if (hasTransit && file_texture == 6) transitTexturesSeen++;
					yield return controller.StartCoroutine(cnt.PrepareGFByTGAName(textures[i].name));
					GF gf = cnt.preparedGF;
					if (gf != null) textures[i].Texture = gf.GetTexture();
                }
			}
			loadingState = state;
		}

        public void ReadSuperObjects(Reader reader) {
            actualWorld = SuperObject.FromOffsetOrRead(globals.off_actualWorld, reader);
            dynamicWorld = SuperObject.FromOffsetOrRead(globals.off_dynamicWorld, reader);
            inactiveDynamicWorld = SuperObject.FromOffsetOrRead(globals.off_inactiveDynamicWorld, reader);
            transitDynamicWorld = SuperObject.FromOffsetOrRead(globals.off_transitDynamicWorld, reader);
            fatherSector = SuperObject.FromOffsetOrRead(globals.off_fatherSector, reader);

            if (actualWorld != null) actualWorld.Gao.name = "Actual World";
            if (dynamicWorld != null) dynamicWorld.Gao.name = "Dynamic World";
            if (inactiveDynamicWorld != null) inactiveDynamicWorld.Gao.name = "Inactive Dynamic World";
            if (transitDynamicWorld != null) transitDynamicWorld.Gao.name = "Transit Dynamic World (perso in fix)";
            if (fatherSector != null) fatherSector.Gao.name = "Father Sector";
        }

        public void ReadAlways(Reader reader) {
            // Parse spawnable SO's
            if (globals.spawnablePersos != null && globals.spawnablePersos.Count > 0) {
                GameObject spawnableParent = new GameObject("Spawnable persos");
                globals.spawnablePersos.ReadEntries(ref reader, (offset) => {
					uint index;
					Pointer off_spawnable_perso;
					if (Settings.s.game == Settings.Game.R2Revolution) {
						off_spawnable_perso = Pointer.Read(reader);
						index = reader.ReadUInt32();
					} else {
						index = reader.ReadUInt32();
						off_spawnable_perso = Pointer.Read(reader);
					}
					Perso perso = null;
                    Pointer.DoAt(ref reader, off_spawnable_perso, () => {
                        perso = Perso.Read(reader, off_spawnable_perso, null);
                        if (perso != null) {
                            perso.Gao.transform.parent = spawnableParent.transform;
                        }
                    });
                    return perso;
                }, LinkedList.Flags.HasHeaderPointers);
            }
        }

        public void ReadFamilies(Reader reader) {
            if (families.Count > 0) {
                familiesRoot = new GameObject("Families");
                familiesRoot.SetActive(false); // Families do not need to be visible
                families.ReadEntries(ref reader, (off_element) => {
                    Family f = Family.Read(reader, off_element);
                    f.Gao.transform.SetParent(familiesRoot.transform, false);
                    return f;
                }, LinkedList.Flags.HasHeaderPointers);
            }
        }

        public void ReadCrossReferences(Reader reader) {
            for (int i = 0; i < sectors.Count; i++) {
                sectors[i].ProcessPointers(reader);
            }
        }

        [System.Serializable]
        public class JSON_AIModel {
            public string name;
            public List<string> rules = new List<string>();
            public List<string> reflexes = new List<string>();
        }

        [System.Serializable]
        public class JSON_ComportData {
            public List<JSON_AIModel> aiModels;
        }

        // Comport names are read from a JSON that contains the Dreamcast comport names
        public void ReadAndFillComportNames(Stream stream) {
			string dataAsJson = new StreamReader(stream).ReadToEnd();
			JSON_ComportData comportData = JsonUtility.FromJson<JSON_ComportData>(dataAsJson);
            foreach(AIModel aiModel in aiModels) {
                if (aiModel.name!=null && aiModel.name!="") {
                    JSON_AIModel jsonAiModel = comportData.aiModels.Find(p => p.name.ToLower() == aiModel.name.ToLower());

                    if (jsonAiModel!=null) {
                        if (aiModel.behaviors_normal != null) {
                            for (int i = 0; i < aiModel.behaviors_normal.Length; i++) {
                                Behavior b = aiModel.behaviors_normal[i];
                                if (b != null && jsonAiModel.rules.Count > i && jsonAiModel.rules[i] != null) {
                                    b.name = jsonAiModel.rules[i];
                                }
                            }
                        }
                        if (aiModel.behaviors_reflex != null) {
                            for (int i = 0; i < aiModel.behaviors_reflex.Length; i++) {
                                Behavior b = aiModel.behaviors_reflex[i];
                                if (b != null && jsonAiModel.reflexes.Count > i && jsonAiModel.reflexes[i] != null) {
                                    b.name = jsonAiModel.reflexes[i];
                                }
                            }
                        }

                    }
                }
            }
        }

        public void AddUncategorizedObjectList(ObjectList objList) {
            if (!uncategorizedObjectLists.Contains(objList)) uncategorizedObjectLists.Add(objList);
            objList.Gao.transform.SetParent(familiesRoot.transform);
        }

        protected IEnumerator PrepareFile(string path) {
            if (FileSystem.mode == FileSystem.Mode.Web) {
                string state = loadingState;
                loadingState = state + "\nDownloading file: " + path;
                yield return controller.StartCoroutine(FileSystem.DownloadFile(path));
                loadingState = state;
                yield return null;
            }
		}

		protected IEnumerator PrepareBigFile(string path, int cacheLength) {
			if (FileSystem.mode == FileSystem.Mode.Web) {
				string state = loadingState;
				loadingState = state + "\nInitializing bigfile: " + path + " (Cache size: " + Util.SizeSuffix(cacheLength, 0) + ")";
				yield return controller.StartCoroutine(FileSystem.InitBigFile(path, cacheLength));
				loadingState = state;
				yield return null;
			}
		}

		public string ConvertCase(string path, Settings.CapsType capsType) {
			Settings.Caps caps = Settings.Caps.Normal;
			if (Settings.s.caps != null && Settings.s.caps.ContainsKey(capsType)) {
				caps = Settings.s.caps[capsType];
			} else if (Settings.s.caps != null && Settings.s.caps.ContainsKey(Settings.CapsType.All)) {
				caps = Settings.s.caps[Settings.CapsType.All];
			}
			switch (caps) {
				case Settings.Caps.All:
					return path.ToUpper();
				case Settings.Caps.None:
					return path.ToLower();
				case Settings.Caps.AllExceptExtension:
					if (path.LastIndexOf('.') > 0) {
						string pathWithoutExtension = path.Substring(0, path.LastIndexOf('.')).ToUpper();
						return pathWithoutExtension + path.Substring(path.LastIndexOf('.'));
					} else return path.ToUpper();
				default:
					return path;
			}
		}
	}
}
