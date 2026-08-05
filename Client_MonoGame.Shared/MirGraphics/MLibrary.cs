using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.IO.Compression;
using Frame = MonoShare.MirObjects.Frame;
using MonoShare.MirObjects;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using MonoShare.Share.Extensions;
using System.Collections;

namespace MonoShare.MirGraphics
{
    public static class Libraries
    {
        public static bool Loaded;
        public static int Count, Progress;

        public static readonly MLibrary
            ChrSel = new MLibrary(Settings.DataPath + "ChrSel"),
            Prguse = new MLibrary(Settings.DataPath + "Prguse"),
            Prguse2 = new MLibrary(Settings.DataPath + "Prguse2"),
            Prguse3 = new MLibrary(Settings.DataPath + "Prguse3"),
            BuffIcon = new MLibrary(Settings.DataPath + "BuffIcon"),
            Help = new MLibrary(Settings.DataPath + "Help"),
            MiniMap = new MLibrary(Settings.DataPath + "MMap"),
            Title = new MLibrary(Settings.DataPath + "Title"),
            MagIcon = new MLibrary(Settings.DataPath + "MagIcon"),
            MagIcon2 = new MLibrary(Settings.DataPath + "MagIcon2"),
            Magic = new MLibrary(Settings.DataPath + "Magic"),
            Magic2 = new MLibrary(Settings.DataPath + "Magic2"),
            Magic3 = new MLibrary(Settings.DataPath + "Magic3"),
            Effect = new MLibrary(Settings.DataPath + "Effect"),
            MagicC = new MLibrary(Settings.DataPath + "MagicC"),
            //Magic11 = new MLibrary(Settings.DataPath + "Magic11"),
            GuildSkill = new MLibrary(Settings.DataPath + "GuildSkill");

        public static readonly MLibrary
            Background = new MLibrary(Settings.DataPath + "Background");


        public static readonly MLibrary
            Dragon = new MLibrary(Settings.DataPath + "Dragon");

        //Map
        public static readonly MLibrary[] MapLibs = new MLibrary[400];

        //Items
        public static readonly MLibrary
            Items = new MLibrary(Settings.DataPath + "Items"),
            StateItems = new MLibrary(Settings.DataPath + "StateItem"),
            FloorItems = new MLibrary(Settings.DataPath + "DNItems");

        //Deco
        public static readonly MLibrary
            Deco = new MLibrary(Settings.DataPath + "Deco");

        public static MLibrary[] CArmours,
                                          CWeapons,
                                          CWeaponEffect,
                                          CHair,
                                          CHumEffect,
                                          AArmours,
                                          AWeaponsL,
                                          AWeaponsR,
                                          AHair,
                                          AHumEffect,
                                          ARArmours,
                                          ARWeapons,
                                          ARWeaponsS,
                                          ARHair,
                                          ARHumEffect,
                                          Monsters,
                                          Gates,
                                          Flags,
                                          Mounts,
                                          NPCs,
                                          Fishing,
                                          Pets,
                                          Transform,
                                          TransformMounts,
                                          TransformEffect,
                                          TransformWeaponEffect;

        static Libraries()
        {
            //Wiz/War/Tao
            InitLibrary(ref CArmours, Settings.CArmourPath, "00");
            InitLibrary(ref CHair, Settings.CHairPath, "00");
            InitLibrary(ref CWeapons, Settings.CWeaponPath, "00");
            InitLibrary(ref CWeaponEffect, Settings.CWeaponEffectPath, "00");
            InitLibrary(ref CHumEffect, Settings.CHumEffectPath, "00");

            //Assassin
            InitLibrary(ref AArmours, Settings.AArmourPath, "00");
            InitLibrary(ref AHair, Settings.AHairPath, "00");
            InitLibrary(ref AWeaponsL, Settings.AWeaponPath, "00", " L");
            InitLibrary(ref AWeaponsR, Settings.AWeaponPath, "00", " R");
            InitLibrary(ref AHumEffect, Settings.AHumEffectPath, "00");

            //Archer
            InitLibrary(ref ARArmours, Settings.ARArmourPath, "00");
            InitLibrary(ref ARHair, Settings.ARHairPath, "00");
            InitLibrary(ref ARWeapons, Settings.ARWeaponPath, "00");
            InitLibrary(ref ARWeaponsS, Settings.ARWeaponPath, "00", " S");
            InitLibrary(ref ARHumEffect, Settings.ARHumEffectPath, "00");

            //Other
            InitLibrary(ref Monsters, Settings.MonsterPath, "000");
            InitLibrary(ref Gates, Settings.GatePath, "00");
            InitLibrary(ref NPCs, Settings.NPCPath, "00");
            InitLibrary(ref Mounts, Settings.MountPath, "00");
            InitLibrary(ref Fishing, Settings.FishingPath, "00");
            InitLibrary(ref Pets, Settings.PetsPath, "00");
            InitLibrary(ref Transform, Settings.TransformPath, "00");
            InitLibrary(ref TransformMounts, Settings.TransformMountsPath, "00");
            InitLibrary(ref TransformEffect, Settings.TransformEffectPath, "00");
            InitLibrary(ref TransformWeaponEffect, Settings.TransformWeaponEffectPath, "00");

            #region Maplibs
            //wemade mir2 (allowed from 0-99)
            MapLibs[0] = new MLibrary(Settings.DataPath + "Map\\WemadeMir2\\Tiles");
            MapLibs[1] = new MLibrary(Settings.DataPath + "Map\\WemadeMir2\\Smtiles");
            MapLibs[2] = new MLibrary(Settings.DataPath + "Map\\WemadeMir2\\Objects");
            for (int i = 2; i < 27; i++)
            {
                MapLibs[i + 1] = new MLibrary(Settings.DataPath + "Map\\WemadeMir2\\Objects" + i.ToString());
            }
            //shanda mir2 (allowed from 100-199)
            MapLibs[100] = new MLibrary(Settings.DataPath + "Map\\ShandaMir2\\Tiles");
            for (int i = 1; i < 10; i++)
            {
                MapLibs[100 + i] = new MLibrary(Settings.DataPath + "Map\\ShandaMir2\\Tiles" + (i + 1));
            }
            MapLibs[110] = new MLibrary(Settings.DataPath + "Map\\ShandaMir2\\SmTiles");
            for (int i = 1; i < 10; i++)
            {
                MapLibs[110 + i] = new MLibrary(Settings.DataPath + "Map\\ShandaMir2\\SmTiles" + (i + 1));
            }
            MapLibs[120] = new MLibrary(Settings.DataPath + "Map\\ShandaMir2\\Objects");
            for (int i = 1; i < 31; i++)
            {
                MapLibs[120 + i] = new MLibrary(Settings.DataPath + "Map\\ShandaMir2\\Objects" + (i + 1));
            }
            MapLibs[190] = new MLibrary(Settings.DataPath + "Map\\ShandaMir2\\AniTiles1");
            //wemade mir3 (allowed from 200-299)
            string[] Mapstate = { "", "wood\\", "sand\\", "snow\\", "forest\\" };
            for (int i = 0; i < Mapstate.Length; i++)
            {
                MapLibs[200 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Tilesc");
                MapLibs[201 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Tiles30c");
                MapLibs[202 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Tiles5c");
                MapLibs[203 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Smtilesc");
                MapLibs[204 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Housesc");
                MapLibs[205 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Cliffsc");
                MapLibs[206 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Dungeonsc");
                MapLibs[207 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Innersc");
                MapLibs[208 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Furnituresc");
                MapLibs[209 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Wallsc");
                MapLibs[210 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "smObjectsc");
                MapLibs[211 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Animationsc");
                MapLibs[212 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Object1c");
                MapLibs[213 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\WemadeMir3\\" + Mapstate[i] + "Object2c");
            }
            Mapstate = new string[] { "", "wood", "sand", "snow", "forest" };
            //shanda mir3 (allowed from 300-399)
            for (int i = 0; i < Mapstate.Length; i++)
            {
                MapLibs[300 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Tilesc" + Mapstate[i]);
                MapLibs[301 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Tiles30c" + Mapstate[i]);
                MapLibs[302 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Tiles5c" + Mapstate[i]);
                MapLibs[303 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Smtilesc" + Mapstate[i]);
                MapLibs[304 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Housesc" + Mapstate[i]);
                MapLibs[305 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Cliffsc" + Mapstate[i]);
                MapLibs[306 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Dungeonsc" + Mapstate[i]);
                MapLibs[307 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Innersc" + Mapstate[i]);
                MapLibs[308 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Furnituresc" + Mapstate[i]);
                MapLibs[309 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Wallsc" + Mapstate[i]);
                MapLibs[310 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "smObjectsc" + Mapstate[i]);
                MapLibs[311 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Animationsc" + Mapstate[i]);
                MapLibs[312 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Object1c" + Mapstate[i]);
                MapLibs[313 + (i * 15)] = new MLibrary(Settings.DataPath + "Map\\ShandaMir3\\" + "Object2c" + Mapstate[i]);
            }
            #endregion

            LoadLibraries();

            Thread thread = new Thread(LoadGameLibraries) { IsBackground = true };
            thread.Start();
        }

        static void InitLibrary(ref MLibrary[] library, string path, string toStringValue, string suffix = "")
        {
            path = Settings.ResolveLibraryDirectory(path);

            if (string.IsNullOrWhiteSpace(path))
            {
                library = Array.Empty<MLibrary>();
                return;
            }

            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception)
            {
                library = Array.Empty<MLibrary>();
                return;
            }

            if (Directory.Exists(path))
            {
                if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
                    path += Path.DirectorySeparatorChar;

                var allFiles = Directory.GetFiles(path, "*" + suffix + MLibrary.Extention, SearchOption.TopDirectoryOnly)
                    .OrderBy(x => int.Parse(Regex.Match(x, @"\d+").Value));

                var lastFile = allFiles.Count() > 0 ? Path.GetFileName(allFiles.Last()) : "0";

                var count = int.Parse(Regex.Match(lastFile, @"\d+").Value) + 1;

                library = new MLibrary[count];

                for (int i = 0; i < count; i++)
                {
                    library[i] = new MLibrary(path + i.ToString(toStringValue) + suffix);
                }
            }


            //if (!string.IsNullOrEmpty(Properties.Resources.ResourcesFileName))
            //{
            //    string[] resourcesInfo = Properties.Resources.ResourcesFileName.Split('\r');
            //    var item = resourcesInfo.FirstOrDefault(ss => ss.Contains(tempPath));
            //    if (item != null)
            //    {
            //        int count = Convert.ToInt32(item.Split('|')[1]);
            //        if (library == null || count != library.Length)
            //        {
            //            library = new MLibrary[count];
            //            for (int i = 0; i < count; i++)
            //            {
            //                library[i] = new MLibrary(tempPath + i.ToString(toStringValue) + suffix);
            //            }
            //        }
            //    }
            //}
        }

        private static readonly object IndexedLibraryResizeGate = new object();

        public static bool EnsureMonsterLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref Monsters, Settings.MonsterPath, "000", string.Empty, index, maxIndex: 2000);
        }

        public static bool EnsureGateLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref Gates, Settings.GatePath, "00", string.Empty, index, maxIndex: 500);
        }

        public static bool EnsurePetLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref Pets, Settings.PetsPath, "00", string.Empty, index, maxIndex: 500);
        }

        public static bool EnsureNpcLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref NPCs, Settings.NPCPath, "00", string.Empty, index, maxIndex: 5000);
        }

        public static bool EnsureFlagLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref Flags, Settings.FlagPath, "00", string.Empty, index, maxIndex: Globals.FlagIndexCount);
        }

        public static bool EnsureCArmourLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref CArmours, Settings.CArmourPath, "00", string.Empty, index, maxIndex: 2000);
        }

        public static bool EnsureCHairLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref CHair, Settings.CHairPath, "00", string.Empty, index, maxIndex: 500);
        }

        public static bool EnsureCWeaponLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref CWeapons, Settings.CWeaponPath, "00", string.Empty, index, maxIndex: 2000);
        }

        public static bool EnsureCWeaponEffectLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref CWeaponEffect, Settings.CWeaponEffectPath, "00", string.Empty, index, maxIndex: 2000);
        }

        public static bool EnsureCHumEffectLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref CHumEffect, Settings.CHumEffectPath, "00", string.Empty, index, maxIndex: 2000);
        }

        public static bool EnsureAArmourLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref AArmours, Settings.AArmourPath, "00", string.Empty, index, maxIndex: 2000);
        }

        public static bool EnsureAHairLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref AHair, Settings.AHairPath, "00", string.Empty, index, maxIndex: 500);
        }

        public static bool EnsureAWeaponLLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref AWeaponsL, Settings.AWeaponPath, "00", " L", index, maxIndex: 2000);
        }

        public static bool EnsureAWeaponRLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref AWeaponsR, Settings.AWeaponPath, "00", " R", index, maxIndex: 2000);
        }

        public static bool EnsureAHumEffectLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref AHumEffect, Settings.AHumEffectPath, "00", string.Empty, index, maxIndex: 2000);
        }

        public static bool EnsureARArmourLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref ARArmours, Settings.ARArmourPath, "00", string.Empty, index, maxIndex: 2000);
        }

        public static bool EnsureARHairLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref ARHair, Settings.ARHairPath, "00", string.Empty, index, maxIndex: 500);
        }

        public static bool EnsureARWeaponLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref ARWeapons, Settings.ARWeaponPath, "00", string.Empty, index, maxIndex: 2000);
        }

        public static bool EnsureARWeaponsSLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref ARWeaponsS, Settings.ARWeaponPath, "00", " S", index, maxIndex: 2000);
        }

        public static bool EnsureARHumEffectLibraryIndex(int index)
        {
            return EnsureIndexedLibrary(ref ARHumEffect, Settings.ARHumEffectPath, "00", string.Empty, index, maxIndex: 2000);
        }

        private static bool EnsureIndexedLibrary(ref MLibrary[] library, string basePath, string toStringValue, string suffix, int index, int maxIndex)
        {
            if (index < 0)
                return false;

            if (index > maxIndex)
                return false;

            lock (IndexedLibraryResizeGate)
            {
                if (library == null)
                    library = Array.Empty<MLibrary>();

                if (index >= library.Length)
                {
                    int oldLength = library.Length;
                    Array.Resize(ref library, index + 1);

                    for (int i = oldLength; i < library.Length; i++)
                    {
                        library[i] = new MLibrary(basePath + i.ToString(toStringValue) + suffix);
                    }
                }
                else if (library[index] == null)
                {
                    library[index] = new MLibrary(basePath + index.ToString(toStringValue) + suffix);
                }
            }

            return library.Length > index && library[index] != null;
        }

        static void LoadLibraries()
        {
            ChrSel.Initialize();
            Progress++;

            Prguse.Initialize();
            Progress++;

            Prguse2.Initialize();
            Progress++;

            Prguse3.Initialize();
            Progress++;

            Title.Initialize();
            Progress++;
        }

        private static void LoadGameLibraries()
        {
            Count = MapLibs.Length + Monsters.Length + Gates.Length + NPCs.Length + CArmours.Length +
                CHair.Length + CWeapons.Length + CWeaponEffect.Length + AArmours.Length + AHair.Length + AWeaponsL.Length + AWeaponsR.Length +
                ARArmours.Length + ARHair.Length + ARWeapons.Length + ARWeaponsS.Length +
                CHumEffect.Length + AHumEffect.Length + ARHumEffect.Length + Mounts.Length + Fishing.Length + Pets.Length +
                Transform.Length + TransformMounts.Length + TransformEffect.Length + TransformWeaponEffect.Length + 17;

            Dragon.Initialize();
            Progress++;

            BuffIcon.Initialize();
            Progress++;

            Help.Initialize();
            Progress++;

            MiniMap.Initialize();
            Progress++;

            MagIcon.Initialize();
            Progress++;
            MagIcon2.Initialize();
            Progress++;

            Magic.Initialize();
            Progress++;
            Magic2.Initialize();
            Progress++;
            Magic3.Initialize();
            Progress++;
            MagicC.Initialize();
            Progress++;

            //Magic11.Initialize();
            //Progress++;

            Effect.Initialize();
            Progress++;

            GuildSkill.Initialize();
            Progress++;

            Background.Initialize();
            Progress++;

            Deco.Initialize();
            Progress++;

            Items.Initialize();
            Progress++;
            StateItems.Initialize();
            Progress++;
            FloorItems.Initialize();
            Progress++;

            for (int i = 0; i < MapLibs.Length; i++)
            {
                if (MapLibs[i] == null)
                    MapLibs[i] = new MLibrary("");
                else
                    MapLibs[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < Monsters.Length; i++)
            {
                Monsters[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < Gates.Length; i++)
            {
                Gates[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < NPCs.Length; i++)
            {
                NPCs[i].Initialize();
                Progress++;
            }


            for (int i = 0; i < CArmours.Length; i++)
            {
                CArmours[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < CHair.Length; i++)
            {
                CHair[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < CWeapons.Length; i++)
            {
                CWeapons[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < CWeaponEffect.Length; i++)
            {
                CWeaponEffect[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < AArmours.Length; i++)
            {
                AArmours[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < AHair.Length; i++)
            {
                AHair[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < AWeaponsL.Length; i++)
            {
                AWeaponsL[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < AWeaponsR.Length; i++)
            {
                AWeaponsR[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < ARArmours.Length; i++)
            {
                ARArmours[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < ARHair.Length; i++)
            {
                ARHair[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < ARWeapons.Length; i++)
            {
                ARWeapons[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < ARWeaponsS.Length; i++)
            {
                ARWeaponsS[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < CHumEffect.Length; i++)
            {
                CHumEffect[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < AHumEffect.Length; i++)
            {
                AHumEffect[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < ARHumEffect.Length; i++)
            {
                ARHumEffect[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < Mounts.Length; i++)
            {
                Mounts[i].Initialize();
                Progress++;
            }


            for (int i = 0; i < Fishing.Length; i++)
            {
                Fishing[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < Pets.Length; i++)
            {
                Pets[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < Transform.Length; i++)
            {
                Transform[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < TransformEffect.Length; i++)
            {
                TransformEffect[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < TransformWeaponEffect.Length; i++)
            {
                TransformWeaponEffect[i].Initialize();
                Progress++;
            }

            for (int i = 0; i < TransformMounts.Length; i++)
            {
                TransformMounts[i].Initialize();
                Progress++;
            }

            Loaded = true;

        }

    }

    public sealed class MLibrary
    {
        public const string Extention = ".Lib";
        public const int LibVersion = 3;

        // 移动端首进地图时，贴图创建（解压 + Texture2D.SetData）可能在单帧内集中触发，导致主线程长卡，
        // 进而 Network.Process 无法按时发送 KeepAlive，表现为“黑屏几秒后超时掉线/退出”。
        // 这里为贴图创建增加“帧内预算”，让创建分摊到多帧完成，以换取连接稳定性。
        private static int MobileMaxTextureCreatesPerFrame
        {
            get
            {
                try
                {
                    return Math.Clamp(Settings.MobileMaxTextureCreatesPerFrame, 1, 120);
                }
                catch
                {
                    return 6;
                }
            }
        }
        private static long _mobileTextureBudgetFrameTimeMs;
        private static int _mobileTextureCreatesThisFrame;
        private static long _nextMobileTextureBudgetLogTick;

        private static bool IsMobileTextureCreateBudgetExhaustedThisFrame()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return false;

            long frameTimeMs = CMain.Time;
            if (frameTimeMs != _mobileTextureBudgetFrameTimeMs)
                return false;

            return _mobileTextureCreatesThisFrame >= MobileMaxTextureCreatesPerFrame;
        }

        private static readonly BlendState InverseSourceColorBlendState = new BlendState
        {
            ColorSourceBlend = Blend.BlendFactor,
            ColorDestinationBlend = Blend.InverseSourceColor,
            ColorBlendFunction = BlendFunction.Add,
        };

        private readonly string _fileName;
        private readonly string _microRelativeFilePath;

        private MImage[] _images;
        private FrameSet _frames;
        private int[] _indexList;
        private int _count;
        private bool _initialized;

        private BinaryReader _reader;
        private FileStream _fStream;
        private readonly SemaphoreSlim _fileLock;

        private static bool TryConsumeMobileTextureCreateBudget()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return true;

            long frameTimeMs = CMain.Time;
            if (frameTimeMs != _mobileTextureBudgetFrameTimeMs)
            {
                _mobileTextureBudgetFrameTimeMs = frameTimeMs;
                _mobileTextureCreatesThisFrame = 0;
            }

            if (_mobileTextureCreatesThisFrame >= MobileMaxTextureCreatesPerFrame)
                return false;

            _mobileTextureCreatesThisFrame++;
            return true;
        }

        private void TryLogMobileTextureBudgetExhaustedIfDue(int imageIndex)
        {
            if (!Settings.LogErrors)
                return;

            try
            {
                long nowTick = Environment.TickCount64;
                if (nowTick < _nextMobileTextureBudgetLogTick)
                    return;

                _nextMobileTextureBudgetLogTick = nowTick + 2000;

                string fileName = string.Empty;
                try
                {
                    fileName = Path.GetFileName(_fileName) ?? string.Empty;
                }
                catch
                {
                    fileName = _fileName ?? string.Empty;
                }

                CMain.SaveLog($"MLibrary: 本帧贴图创建预算已用尽，延后到后续帧（避免卡顿/掉线）。Lib={fileName} Index={imageIndex} Budget={MobileMaxTextureCreatesPerFrame}/frame");
            }
            catch
            {
            }
        }

        public FrameSet Frames
        {
            get { return _frames; }
        }

        public MLibrary(string filename)
        {
            _fileName = Settings.ResolveLibraryFile(filename + Extention);
            _microRelativeFilePath = ResolveMicroRelativeFilePath(_fileName, filename + Extention);

            try
            {
                _fileLock = MicroLibraryHelper.GetOrCreateFileLock(_fileName);
            }
            catch
            {
                _fileLock = null;
            }
        }

        private static string ResolveMicroRelativeFilePath(string localFilePath, string fallbackPath)
        {
            string fallback = NormalizeMicroRelativePath(fallbackPath);

            if (string.IsNullOrWhiteSpace(localFilePath))
                return fallback;

            try
            {
                string fullPath = Path.GetFullPath(localFilePath);

                string dataRoot = EnsureTrailingSeparator(Path.GetFullPath(ClientResourceLayout.DataRoot));
                if (fullPath.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string relative = fullPath.Substring(dataRoot.Length);
                    return NormalizeMicroRelativePath("Data/" + NormalizeMicroRelativePath(relative));
                }

                string libraryCacheRoot = EnsureTrailingSeparator(Path.GetFullPath(ClientResourceLayout.LibraryCacheRoot));
                if (fullPath.StartsWith(libraryCacheRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string relative = fullPath.Substring(libraryCacheRoot.Length);
                    return NormalizeMicroRelativePath("Data/" + NormalizeMicroRelativePath(relative));
                }
            }
            catch
            {
                return fallback;
            }

            return fallback;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalized + Path.DirectorySeparatorChar;
        }

        public void Initialize()
        {
            _initialized = true;

            if (!File.Exists(_fileName))
            {
                _initialized = false;
                return;
            }


            bool lockHeld = false;
            try
            {
                if (_fileLock != null)
                {
                    if (!_fileLock.Wait(0))
                    {
                        _initialized = false;
                        return;
                    }
                    lockHeld = true;
                }
            }
            catch
            {
                lockHeld = false;
                _initialized = false;
                return;
            }

            try
            {
                try
                {
                    _reader?.Dispose();
                }
                catch
                {
                }

                try
                {
                    _fStream?.Dispose();
                }
                catch
                {
                }

                _reader = null;
                _fStream = null;

                _fStream = new FileStream(_fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                _reader = new BinaryReader(_fStream);
                int currentVersion = _reader.ReadInt32();
                if (currentVersion < 2)
                {
                    //System.Windows.Forms.MessageBox.Show("Wrong version, expecting lib version: " + LibVersion.ToString() + " found version: " + currentVersion.ToString() + ".", _fileName, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error, System.Windows.Forms.MessageBoxDefaultButton.Button1);
                    //System.Windows.Forms.Application.Exit();
                    _initialized = false;
                    return;
                }
                _count = _reader.ReadInt32();

                int frameSeek = 0;
                if (currentVersion >= 3)
                {
                    frameSeek = _reader.ReadInt32();
                }

                _images = new MImage[_count];
                _indexList = new int[_count];

                for (int i = 0; i < _count; i++)
                    _indexList[i] = _reader.ReadInt32();

                if (currentVersion >= 3)
                {
                    _fStream.Seek(frameSeek, SeekOrigin.Begin);

                    var frameCount = _reader.ReadInt32();

                    if (frameCount > 0)
                    {
                        _frames = new FrameSet();
                        for (int i = 0; i < frameCount; i++)
                        {
                            _frames.Add((MirAction)_reader.ReadByte(), new Frame(_reader));
                        }
                    }
                }
            }
            catch (Exception)
            {
                _initialized = false;
                try
                {
                    _reader?.Dispose();
                }
                catch
                {
                }

                try
                {
                    _fStream?.Dispose();
                }
                catch
                {
                }

                _reader = null;
                _fStream = null;
                if (MicroLibraryHelper.IsConfigured)
                    MicroLibraryHelper.QueueLibraryHeaderDownload(_microRelativeFilePath, _fileName);
                return;
            }
            finally
            {
                if (lockHeld)
                {
                    try
                    {
                        _fileLock.Release();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private bool CheckImage(int index)
        {
            if (!EnsureInitialized())
                return false;

            if (_images == null || index < 0 || index >= _images.Length)
                return false;

            if (MicroLibraryHelper.IsConfigured && MicroLibraryHelper.IsLibraryImageDownloadPending(_microRelativeFilePath, index))
                return false;

            // 帧预算已用尽时，避免每个瓦片/对象都去抢文件锁与读头（否则首进地图时会在同一帧内触发海量 CheckImage，造成长卡与掉线）。
            if (IsMobileTextureCreateBudgetExhaustedThisFrame())
            {
                MImage cached = _images[index];
                if (cached != null && cached.TextureValid)
                    return true;

                TryLogMobileTextureBudgetExhaustedIfDue(index);
                return false;
            }

            bool lockHeld = false;
            try
            {
                if (_fileLock != null)
                {
                    if (!_fileLock.Wait(0))
                        return false;
                    lockHeld = true;
                }
            }
            catch
            {
                lockHeld = false;
                return false;
            }

            try
            {
                if (_fStream == null || _reader == null || _indexList == null)
                    return false;

                int offset = _indexList[index];
                if (offset < 0 || offset + 17 > _fStream.Length)
                {
                    _images[index] = null;
                    _initialized = false;
                    if (MicroLibraryHelper.IsConfigured)
                    {
                        MicroLibraryHelper.QueueLibraryHeaderDownload(_microRelativeFilePath, _fileName);
                        MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                    }
                    return false;
                }

                if (_images[index] == null)
                {
                    _fStream.Position = offset;
                    try
                    {
                        _images[index] = new MImage(_reader);
                    }
                    catch
                    {
                        _images[index] = null;
                        if (MicroLibraryHelper.IsConfigured)
                            MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                        return false;
                    }
                }

                MImage mi = _images[index];
                if (mi == null)
                    return false;

                if (!mi.TextureValid)
                {
                    if (!TryConsumeMobileTextureCreateBudget())
                    {
                        TryLogMobileTextureBudgetExhaustedIfDue(index);
                        return false;
                    }

                    if ((mi.Width == 0) || (mi.Height == 0) || mi.Length <= 0)
                    {
                        _images[index] = null;
                        if (MicroLibraryHelper.IsConfigured)
                            MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                        return false;
                    }

                    long dataOffset = (long)offset + 17;
                    if (dataOffset < 0 || dataOffset > _fStream.Length)
                    {
                        _images[index] = null;
                        if (MicroLibraryHelper.IsConfigured)
                            MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                        return false;
                    }

                    _fStream.Seek(dataOffset, SeekOrigin.Begin);
                    try
                    {
                        mi.CreateTexture(_reader);
                    }
                    catch
                    {
                        _images[index] = null;
                        if (MicroLibraryHelper.IsConfigured)
                            MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                if (lockHeld)
                {
                    try
                    {
                        _fileLock.Release();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public Point GetOffSet(int index)
        {
            if (!EnsureInitialized())
                return Point.Empty;

            if (_images == null || index < 0 || index >= _images.Length)
                return Point.Empty;

            if (MicroLibraryHelper.IsConfigured && MicroLibraryHelper.IsLibraryImageDownloadPending(_microRelativeFilePath, index))
                return Point.Empty;

            bool lockHeld = false;
            try
            {
                if (_fileLock != null)
                {
                    if (!_fileLock.Wait(0))
                        return Point.Empty;
                    lockHeld = true;
                }
            }
            catch
            {
                lockHeld = false;
                return Point.Empty;
            }

            try
            {
                if (_fStream == null || _reader == null || _indexList == null)
                    return Point.Empty;

                int offset = _indexList[index];
                if (offset < 0 || offset + 17 > _fStream.Length)
                {
                    _images[index] = null;
                    _initialized = false;
                    if (MicroLibraryHelper.IsConfigured)
                    {
                        MicroLibraryHelper.QueueLibraryHeaderDownload(_microRelativeFilePath, _fileName);
                        MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                    }
                    return Point.Empty;
                }

                if (_images[index] == null)
                {
                    _fStream.Seek(offset, SeekOrigin.Begin);
                    try
                    {
                        _images[index] = new MImage(_reader);
                    }
                    catch
                    {
                        _images[index] = null;
                        if (MicroLibraryHelper.IsConfigured)
                            MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                        return Point.Empty;
                    }
                }

                if ((_images[index].Width == 0) || (_images[index].Height == 0))
                {
                    _images[index] = null;
                    if (MicroLibraryHelper.IsConfigured)
                        MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                    return Point.Empty;
                }

                return new Point(_images[index].X, _images[index].Y);
            }
            finally
            {
                if (lockHeld)
                {
                    try
                    {
                        _fileLock.Release();
                    }
                    catch
                    {
                    }
                }
            }
        }
        public Size GetSize(int index)
        {
            if (!EnsureInitialized())
                return Size.Empty;

            if (_images == null || index < 0 || index >= _images.Length)
                return Size.Empty;

            if (MicroLibraryHelper.IsConfigured && MicroLibraryHelper.IsLibraryImageDownloadPending(_microRelativeFilePath, index))
                return Size.Empty;

            bool lockHeld = false;
            try
            {
                if (_fileLock != null)
                {
                    if (!_fileLock.Wait(0))
                        return Size.Empty;
                    lockHeld = true;
                }
            }
            catch
            {
                lockHeld = false;
                return Size.Empty;
            }

            try
            {
                if (_fStream == null || _reader == null || _indexList == null)
                    return Size.Empty;

                int offset = _indexList[index];
                if (offset < 0 || offset + 17 > _fStream.Length)
                {
                    _images[index] = null;
                    _initialized = false;
                    if (MicroLibraryHelper.IsConfigured)
                    {
                        MicroLibraryHelper.QueueLibraryHeaderDownload(_microRelativeFilePath, _fileName);
                        MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                    }
                    return Size.Empty;
                }

                if (_images[index] == null)
                {
                    _fStream.Seek(offset, SeekOrigin.Begin);
                    try
                    {
                        _images[index] = new MImage(_reader);
                    }
                    catch
                    {
                        _images[index] = null;
                        if (MicroLibraryHelper.IsConfigured)
                            MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                        return Size.Empty;
                    }
                }

                if ((_images[index].Width == 0) || (_images[index].Height == 0))
                {
                    _images[index] = null;
                    if (MicroLibraryHelper.IsConfigured)
                        MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                    return Size.Empty;
                }

                return new Size(_images[index].Width, _images[index].Height);
            }
            finally
            {
                if (lockHeld)
                {
                    try
                    {
                        _fileLock.Release();
                    }
                    catch
                    {
                    }
                }
            }
        }
        public Size GetTrueSize(int index)
        {
            if (!EnsureInitialized())
                return Size.Empty;

            if (_images == null || index < 0 || index >= _images.Length)
                return Size.Empty;

            if (MicroLibraryHelper.IsConfigured && MicroLibraryHelper.IsLibraryImageDownloadPending(_microRelativeFilePath, index))
                return Size.Empty;

            bool lockHeld = false;
            try
            {
                if (_fileLock != null)
                {
                    if (!_fileLock.Wait(0))
                        return Size.Empty;
                    lockHeld = true;
                }
            }
            catch
            {
                lockHeld = false;
                return Size.Empty;
            }

            try
            {
                if (_fStream == null || _reader == null || _indexList == null)
                    return Size.Empty;

                int offset = _indexList[index];
                if (offset < 0 || offset + 17 > _fStream.Length)
                {
                    _images[index] = null;
                    _initialized = false;
                    if (MicroLibraryHelper.IsConfigured)
                    {
                        MicroLibraryHelper.QueueLibraryHeaderDownload(_microRelativeFilePath, _fileName);
                        MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                    }
                    return Size.Empty;
                }

                if (_images[index] == null)
                {
                    _fStream.Position = offset;
                    try
                    {
                        _images[index] = new MImage(_reader);
                    }
                    catch
                    {
                        _images[index] = null;
                        if (MicroLibraryHelper.IsConfigured)
                            MicroLibraryHelper.QueueLibraryImageDownload(_microRelativeFilePath, _fileName, index);
                        return Size.Empty;
                    }
                }

                MImage mi = _images[index];
                if (mi == null)
                    return Size.Empty;

                if (mi.TrueSize.IsEmpty)
                {
                    // MonoGame 版目前未实现像素级裁剪（VisiblePixel 返回 false），TrueSize 等同于原始宽高。
                    // 这里不应为了取尺寸而强制创建 Texture2D（否则首进地图/对象初始化会大量触发解压与贴图创建，造成主线程卡顿与掉线）。
                    mi.TrueSize = new Size(
                        mi.Width <= 0 ? 0 : (int)mi.Width,
                        mi.Height <= 0 ? 0 : (int)mi.Height);
                }

                return mi.TrueSize;
            }
            finally
            {
                if (lockHeld)
                {
                    try
                    {
                        _fileLock.Release();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public Texture2D GetTexture(int index)
        {
            if (!CheckImage(index))
                return null;

            MImage mi = _images[index];
            if (mi == null)
                return null;

            mi.CleanTime = CMain.Time + Settings.CleanDelay;
            return mi.Image;
        }

        public bool Touch(int index)
        {
            if (!CheckImage(index))
                return false;

            MImage mi = _images[index];
            if (mi == null)
                return false;

            mi.CleanTime = CMain.Time + Settings.CleanDelay;
            return true;
        }

        private bool EnsureInitialized()
        {
            if (_initialized)
                return true;

            if (File.Exists(_fileName))
            {
                Initialize();
                return _initialized;
            }

            AsynDownLoadResources.CreateInstance().Add(_fileName, Initialize);

            if (MicroLibraryHelper.IsConfigured)
                MicroLibraryHelper.QueueLibraryHeaderDownload(_microRelativeFilePath, _fileName);
            return false;
        }

        private static string NormalizeMicroRelativePath(string microRelativePath)
        {
            string normalized = (microRelativePath ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');

            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            return normalized;
        }

        public void Draw(int index, int x, int y)
        {
            if (x >= Settings.ScreenWidth || y >= Settings.ScreenHeight)
                return;

            if (!CheckImage(index))
                return;

            MImage mi = _images[index];
            if (mi == null) return;
            if (x + mi.Width < 0 || y + mi.Height < 0)
                return;
            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)x, (float)y, 0.0F), Color.White);
            CMain.SpriteBatchScope.Begin();
            //Color[] data = new Color[mi.Image.Width * mi.Image.Height];
            //mi.Image.GetData(data);
            //Microsoft.Xna.Framework.Color transparentColor = Microsoft.Xna.Framework.Color.White;
            //for (int i = 0; i < data.Length; i++)
            //{
            //    if (!data[i].Equals(transparentColor))
            //    {
            //        data[i] = Color.Transparent;
            //    }
            //}

            //mi.Image.SetData(data);
            CMain.spriteBatch.Draw(mi.Image,
                                  new Microsoft.Xna.Framework.Vector2(x, y),
                                  new Microsoft.Xna.Framework.Rectangle(0, 0, mi.Width, mi.Height), // 使用构造函数
                                  Microsoft.Xna.Framework.Color.White);

            CMain.SpriteBatchScope.End();
        }
        public void Draw(int index, Point point, Color colour, bool offSet = false)
        {
            if (!CheckImage(index))
                return;

            MImage mi = _images[index];

            if (offSet) point.Offset(mi.X, mi.Y);

            if (point.X >= Settings.ScreenWidth || point.Y >= Settings.ScreenHeight || point.X + mi.Width < 0 || point.Y + mi.Height < 0)
                return;
            //DXManager.Draw(mi, index, point.ToXnaPoint(), colour.ToXnaColor());

            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)point.X, (float)point.Y, 0.0F), colour);
            CMain.SpriteBatchScope.Begin();
            CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Vector2(point.X, point.Y), Microsoft.Xna.Framework.Color.White);
            CMain.SpriteBatchScope.End();
            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }

        public void Draw(int index, Point point, Color colour, bool offSet, float opacity)
        {
            if (!CheckImage(index))
                return;

            MImage mi = _images[index];

            if (offSet) point.Offset(mi.X, mi.Y);

            if (point.X >= Settings.ScreenWidth || point.Y >= Settings.ScreenHeight || point.X + mi.Width < 0 || point.Y + mi.Height < 0)
                return;

            //float oldOpacity = DXManager.Opacity;
            //DXManager.SetOpacity(opacity);
            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)point.X, (float)point.Y, 0.0F), colour);
            //DXManager.SetOpacity(oldOpacity);
            // 设置透明度

            // 在屏幕上绘制纹理

            //DXManager.Draw(mi, index, point.ToXnaPoint(), colour.ToXnaColor());
            CMain.SpriteBatchScope.Begin();
            // 设置透明度
            Microsoft.Xna.Framework.Color color = new Microsoft.Xna.Framework.Color(colour.R, colour.G, colour.B, (int)(opacity * 255.0f));
            CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Vector2(point.X, point.Y), color);
            //CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Vector2(point.X, point.Y), Microsoft.Xna.Framework.Color.White);
            CMain.SpriteBatchScope.End();
            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }

        public void DrawBlend(int index, Point point, Color colour, bool offSet = false, float rate = 1)
        {
            if (!CheckImage(index))
                return;

            MImage mi = _images[index];

            if (offSet) point.Offset(mi.X, mi.Y);

            if (point.X >= Settings.ScreenWidth || point.Y >= Settings.ScreenHeight || point.X + mi.Width < 0 || point.Y + mi.Height < 0)
                return;

            //bool oldBlend = DXManager.Blending;
            //DXManager.SetBlend(true, rate);
            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)point.X, (float)point.Y, 0.0F), colour);
            //DXManager.SetBlend(oldBlend);
            //    CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Rectangle(new Microsoft.Xna.Framework.Point(0, 0),
            //new Microsoft.Xna.Framework.Point(mi.Width, mi.Height)), Microsoft.Xna.Framework.Color.White);



            CMain.SpriteBatchScope.Begin(SpriteSortMode.Deferred, InverseSourceColorBlendState);

            Microsoft.Xna.Framework.Vector2 position = new Microsoft.Xna.Framework.Vector2(point.X, point.Y);

            CMain.spriteBatch.Draw(
                mi.Image,
                position,
                //null,
                Microsoft.Xna.Framework.Color.White
            //0f,
            //Microsoft.Xna.Framework.Vector2.Zero,
            //1f,
            //Microsoft.Xna.Framework.Graphics.SpriteEffects.None,
            //0f
            );

            CMain.SpriteBatchScope.End();

            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }
        public void Draw(int index, Rectangle section, Point point, Color colour, bool offSet)
        {
            if (!CheckImage(index))
                return;

            MImage mi = _images[index];

            if (offSet) point.Offset(mi.X, mi.Y);

            if (point.X >= Settings.ScreenWidth || point.Y >= Settings.ScreenHeight || point.X + section.Width < 0 || point.Y + section.Height < 0)
                return;

            if (section.Right > mi.Width)
                section.Width -= section.Right - mi.Width;

            if (section.Bottom > mi.Height)
                section.Height -= section.Bottom - mi.Height;
            CMain.SpriteBatchScope.Begin();
            var srcRect = new Microsoft.Xna.Framework.Rectangle(section.X, section.Y, section.Width, section.Height);
            CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Vector2(point.X, point.Y), srcRect, colour.ToXnaColor());
            CMain.SpriteBatchScope.End();
            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }
        public void Draw(int index, Rectangle section, Point point, Color colour, float opacity)
        {
            if (!CheckImage(index))
                return;

            MImage mi = _images[index];


            if (point.X >= Settings.ScreenWidth || point.Y >= Settings.ScreenHeight || point.X + section.Width < 0 || point.Y + section.Height < 0)
                return;

            if (section.Right > mi.Width)
                section.Width -= section.Right - mi.Width;

            if (section.Bottom > mi.Height)
                section.Height -= section.Bottom - mi.Height;

            opacity = Math.Clamp(opacity, 0F, 1F);
            int alpha = (int)Math.Round(colour.A * opacity);
            alpha = Math.Clamp(alpha, 0, 255);
            var tint = new Microsoft.Xna.Framework.Color(colour.R, colour.G, colour.B, (byte)alpha);

            CMain.SpriteBatchScope.Begin();
            var srcRect = new Microsoft.Xna.Framework.Rectangle(section.X, section.Y, section.Width, section.Height);
            CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Vector2(point.X, point.Y), srcRect, tint);
            CMain.SpriteBatchScope.End();

            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }

        public void Draw(int index, Rectangle section, Rectangle destination, Color colour, float opacity)
        {
            if (!CheckImage(index))
                return;

            if (destination.Width <= 0 || destination.Height <= 0)
                return;

            MImage mi = _images[index];

            if (destination.X >= Settings.ScreenWidth || destination.Y >= Settings.ScreenHeight ||
                destination.Right < 0 || destination.Bottom < 0)
            {
                return;
            }

            if (section.Right > mi.Width)
                section.Width -= section.Right - mi.Width;

            if (section.Bottom > mi.Height)
                section.Height -= section.Bottom - mi.Height;

            if (section.Width <= 0 || section.Height <= 0)
                return;

            opacity = Math.Clamp(opacity, 0F, 1F);
            int alpha = (int)Math.Round(colour.A * opacity);
            alpha = Math.Clamp(alpha, 0, 255);
            var tint = new Microsoft.Xna.Framework.Color(colour.R, colour.G, colour.B, (byte)alpha);

            CMain.SpriteBatchScope.Begin();
            var srcRect = new Microsoft.Xna.Framework.Rectangle(section.X, section.Y, section.Width, section.Height);
            var destRect = new Microsoft.Xna.Framework.Rectangle(destination.X, destination.Y, destination.Width, destination.Height);
            CMain.spriteBatch.Draw(mi.Image, destRect, srcRect, tint);
            CMain.SpriteBatchScope.End();

            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }
        public void Draw(int index, Point point, Size size, Color colour)
        {
            if (!CheckImage(index))
                return;

            MImage mi = _images[index];

            if (point.X >= Settings.ScreenWidth || point.Y >= Settings.ScreenHeight || point.X + size.Width < 0 || point.Y + size.Height < 0)
                return;

            float scaleX = (float)size.Width / mi.Width;
            float scaleY = (float)size.Height / mi.Height;

            //Microsoft.Xna.Framework.Matrix matrix = Microsoft.Xna.Framework.Matrix.CreateScale(scaleX, scaleY, 0);
            //DXManager.Sprite.Transform = matrix;
            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)point.X / scaleX, (float)point.Y / scaleY, 0.0F), Color.White);
            //DXManager.Sprite.Transform = Matrix.Identity;
            //DXManager.Draw(mi, index, point.ToXnaPoint(), colour.ToXnaColor());


            CMain.SpriteBatchScope.Begin();
            Microsoft.Xna.Framework.Matrix matrix = Microsoft.Xna.Framework.Matrix.CreateScale(scaleX, scaleY, 0);
            CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Vector2(point.X, point.Y), Microsoft.Xna.Framework.Color.White);
            CMain.SpriteBatchScope.End();

            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }

        public void DrawTinted(int index, Point point, Color colour, Color Tint, bool offSet = false)
        {
            if (!CheckImage(index))
                return;

            MImage mi = _images[index];

            if (offSet) point.Offset(mi.X, mi.Y);

            if (point.X >= Settings.ScreenWidth || point.Y >= Settings.ScreenHeight || point.X + mi.Width < 0 || point.Y + mi.Height < 0)
                return;
            CMain.SpriteBatchScope.Begin();
            CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Vector2(point.X, point.Y), new Microsoft.Xna.Framework.Rectangle(new Microsoft.Xna.Framework.Point(0, 0),
                      new Microsoft.Xna.Framework.Point(mi.Width, mi.Height)), Microsoft.Xna.Framework.Color.White);
            CMain.SpriteBatchScope.End();

            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)point.X, (float)point.Y, 0.0F), colour);

            //if (mi.HasMask)
            //{
            //    DXManager.Sprite.Draw(mi.MaskImage, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)point.X, (float)point.Y, 0.0F), Tint);
            //}

            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }

        public void DrawUp(int index, int x, int y)
        {
            if (x >= Settings.ScreenWidth)
                return;

            if (!CheckImage(index))
                return;

            MImage mi = _images[index];
            y -= mi.Height;
            if (y >= Settings.ScreenHeight)
                return;
            if (x + mi.Width < 0 || y + mi.Height < 0)
                return;

            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3(x, y, 0.0F), Color.White);
            CMain.SpriteBatchScope.Begin();
            //CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Vector2(x, y), new Microsoft.Xna.Framework.Rectangle(new Microsoft.Xna.Framework.Point(0, 0),
            //            new Microsoft.Xna.Framework.Point(mi.Width, mi.Height)), Microsoft.Xna.Framework.Color.White);
            CMain.SpriteBatchScope.End();
            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }
        public void DrawUpBlend(int index, Point point)
        {
            if (!CheckImage(index))
                return;

            MImage mi = _images[index];

            point.Y -= mi.Height;


            if (point.X >= Settings.ScreenWidth || point.Y >= Settings.ScreenHeight || point.X + mi.Width < 0 || point.Y + mi.Height < 0)
                return;

            //bool oldBlend = DXManager.Blending;
            //DXManager.SetBlend(true, 1);

            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)point.X, (float)point.Y, 0.0F), Color.White);
            //DXManager.SetBlend(oldBlend);

            CMain.SpriteBatchScope.Begin();
            //        CMain.spriteBatch.Draw(mi.Image, new Microsoft.Xna.Framework.Vector2(point.X, point.Y), new Microsoft.Xna.Framework.Rectangle(new Microsoft.Xna.Framework.Point(0, 0),
            //new Microsoft.Xna.Framework.Point(mi.Width, mi.Height)), Microsoft.Xna.Framework.Color.White);
            CMain.SpriteBatchScope.End();
            mi.CleanTime = CMain.Time + Settings.CleanDelay;
        }

        public bool VisiblePixel(int index, Point point, bool accuate)
        {
            if (!CheckImage(index))
                return false;

            if (accuate)
                return _images[index].VisiblePixel(point);

            int accuracy = 2;

            for (int x = -accuracy; x <= accuracy; x++)
                for (int y = -accuracy; y <= accuracy; y++)
                    if (_images[index].VisiblePixel(new Point(point.X + x, point.Y + y)))
                        return true;

            return false;
        }
    }

    public sealed class MImage
    {
        List<Texture2D> cache = new List<Texture2D>();

        public short Width, Height, X, Y, ShadowX, ShadowY;
        public byte Shadow;
        public int Length;

        public bool TextureValid;
        public Texture2D Image;
        //layer 2:
        public short MaskWidth, MaskHeight, MaskX, MaskY;
        public int MaskLength;

        public Texture2D MaskImage;
        public Boolean HasMask;

        public long CleanTime;
        public Size TrueSize;

        //public unsafe byte* Data;

        public MImage(BinaryReader reader)
        {
            try
            {
                //read layer 1
                Width = reader.ReadInt16();
                Height = reader.ReadInt16();
                X = reader.ReadInt16();
                Y = reader.ReadInt16();
                ShadowX = reader.ReadInt16();
                ShadowY = reader.ReadInt16();
                Shadow = reader.ReadByte();
                Length = reader.ReadInt32();

                //check if there's a second layer and read it
                HasMask = ((Shadow >> 7) == 1);

                if (Length < 0)
                {
                    Width = 0;
                    Height = 0;
                    Length = 0;
                    HasMask = false;
                    MaskLength = 0;
                    return;
                }

                if (HasMask)
                {
                    try
                    {
                        if (reader.BaseStream.CanSeek)
                            reader.BaseStream.Seek(Length, SeekOrigin.Current);
                        else
                            reader.ReadBytes(Length);

                        MaskWidth = reader.ReadInt16();
                        MaskHeight = reader.ReadInt16();
                        MaskX = reader.ReadInt16();
                        MaskY = reader.ReadInt16();
                        MaskLength = reader.ReadInt32();

                        if (MaskLength <= 0)
                        {
                            HasMask = false;
                            MaskLength = 0;
                        }
                    }
                    catch
                    {
                        Width = 0;
                        Height = 0;
                        Length = 0;
                        HasMask = false;
                        MaskLength = 0;
                    }
                }
            }
            catch
            {
                Width = 0;
                Height = 0;
                Length = 0;
                HasMask = false;
                MaskLength = 0;
            }
        }

        public void CreateTexture(BinaryReader reader)
        {
            int w = Width;
            int h = Height;

            byte[] imageData = DecompressImage(reader.ReadBytes(Length));

            Image = CreateTextureFromBytes(w, h, imageData);

            if (HasMask)
            {
                reader.ReadBytes(12);
                w = MaskWidth > 0 ? MaskWidth : Width;
                h = MaskHeight > 0 ? MaskHeight : Height;

                byte[] maskData = DecompressImage(reader.ReadBytes(MaskLength));
                MaskImage = CreateTextureFromBytes(w, h, maskData);
            }

            DXManager.TextureList.Add(this);
            TextureValid = true;

            CleanTime = CMain.Time + Settings.CleanDelay;
        }

        private Texture2D CreateTextureFromBytes(int width, int height, byte[] data)
        {
            if (width <= 0 || height <= 0 || data == null || data.Length == 0)
                return null;

            // 旧实现通过 GPU Readback(GetData) 再交换 R/B，开销巨大且会造成首进地图渲染卡顿甚至断线。
            // 这里改为在 CPU 侧直接交换像素字节序（BGRA <-> RGBA），再一次性 SetData，避免 GPU 同步与双纹理分配。
            // 注意：.Lib 解压出的像素数据沿用旧版 A8R8G8B8(BGRA) 内存顺序，因此需要交换 R/B 才能匹配 MonoGame 的 byte[] SetData 预期。
            int expected = width * height * 4;
            if (data.Length >= expected && expected > 0)
            {
                int limit = Math.Min(data.Length - (data.Length % 4), expected);
                for (int i = 0; i < limit; i += 4)
                {
                    byte b = data[i];
                    data[i] = data[i + 2];
                    data[i + 2] = b;
                }
            }

            var texture = new Texture2D(CMain.graphics.GraphicsDevice, width, height, false, SurfaceFormat.Color);
            texture.SetData(data);
            return texture;
        }

        // 调整 Texture2D 的 RGB 通道顺序
        public static Texture2D AdjustRGBOrder(GraphicsDevice graphicsDevice, Texture2D originalTexture)
        {
            if (originalTexture == null)
            {
                throw new ArgumentNullException(nameof(originalTexture));
            }

            // 获取原始纹理数据
            Microsoft.Xna.Framework.Color[] originalData = new Microsoft.Xna.Framework.Color[originalTexture.Width * originalTexture.Height];
            originalTexture.GetData(originalData);

            // 修改 RGB 通道顺序
            for (int i = 0; i < originalData.Length; i++)
            {
                // 将 RGB 通道重新排列为新的顺序
                Microsoft.Xna.Framework.Color originalColor = originalData[i];
                Microsoft.Xna.Framework.Color adjustedColor = new Microsoft.Xna.Framework.Color();
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    adjustedColor = new Microsoft.Xna.Framework.Color(originalColor.B, originalColor.G, originalColor.R, originalColor.A);
                else
                {
                    //adjustedColor = new Microsoft.Xna.Framework.Color(originalColor.R, originalColor.B, originalColor.G, originalColor.A);
                    //adjustedColor = new Microsoft.Xna.Framework.Color(originalColor.R, originalColor.G, originalColor.B, originalColor.A);
                    //adjustedColor = new Microsoft.Xna.Framework.Color(originalColor.B, originalColor.R, originalColor.G, originalColor.A);
                    adjustedColor = new Microsoft.Xna.Framework.Color(originalColor.B, originalColor.G, originalColor.R, originalColor.A);
                    //adjustedColor = new Microsoft.Xna.Framework.Color(originalColor.G, originalColor.B, originalColor.R, originalColor.A);
                    //adjustedColor = new Microsoft.Xna.Framework.Color(originalColor.G, originalColor.R, originalColor.B, originalColor.A);
                }
                originalData[i] = adjustedColor;
            }

            // 创建新的 Texture2D 并设置修改后的纹理数据
            Texture2D adjustedTexture = new Texture2D(graphicsDevice, originalTexture.Width, originalTexture.Height);
            adjustedTexture.SetData(originalData);

            return adjustedTexture;
        }

        //public unsafe void CreateTexture(BinaryReader reader)
        //{
        //    int w = Width;// + (4 - Width % 4) % 4;
        //    int h = Height;// + (4 - Height % 4) % 4;

        //    //Image = new Texture(DXManager.Device, w, h, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);
        //    //DataRectangle stream = Image.LockRectangle(0, LockFlags.Discard);
        //    //Data = (byte*)stream.Data.DataPointer;

        //    //byte[] decomp = DecompressImage(reader.ReadBytes(Length));

        //    //stream.Data.Write(decomp, 0, decomp.Length);

        //    //stream.Data.Dispose();
        //    ////Image.UnlockRectangle(0);

        //    //if (HasMask)
        //    //{
        //    //    reader.ReadBytes(12);
        //    //    w = Width;// + (4 - Width % 4) % 4;
        //    //    h = Height;// + (4 - Height % 4) % 4;

        //    //    MaskImage = new Texture(DXManager.Device, w, h, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);
        //    //    stream = MaskImage.LockRectangle(0, LockFlags.Discard);

        //    //    decomp = DecompressImage(reader.ReadBytes(Length));

        //    //    stream.Data.Write(decomp, 0, decomp.Length);

        //    //    stream.Data.Dispose();
        //    //    MaskImage.UnlockRectangle(0);
        //    //}

        //    //DXManager.TextureList.Add(this);
        //    //TextureValid = true;

        //    CleanTime = CMain.Time + Settings.CleanDelay;
        //}

        public void DisposeTexture()
        {
            DXManager.TextureList.Remove(this);

            if (Image != null)
            {
                Image.Dispose();
            }

            if (MaskImage != null)
            {
                MaskImage.Dispose();
            }

            TextureValid = false;
            Image = null;
            MaskImage = null;
            //Data = null;
        }

        public bool VisiblePixel(Point p)
        {
            if (p.X < 0 || p.Y < 0 || p.X >= Width || p.Y >= Height)
                return false;

            int w = Width;

            bool result = false;
            //if (Data != null)
            //{
            //    int x = p.X;
            //    int y = p.Y;

            //    int index = (y * (w << 2)) + (x << 2);

            //    byte col = Data[index];

            //    if (col == 0) return false;
            //    else return true;
            //}
            return result;
        }

        public Size GetTrueSize()
        {
            if (TrueSize != Size.Empty) return TrueSize;

            int l = 0, t = 0, r = Width, b = Height;

            bool visible = false;
            for (int x = 0; x < r; x++)
            {
                for (int y = 0; y < b; y++)
                {
                    if (!VisiblePixel(new Point(x, y))) continue;

                    visible = true;
                    break;
                }

                if (!visible) continue;

                l = x;
                break;
            }

            visible = false;
            for (int y = 0; y < b; y++)
            {
                for (int x = l; x < r; x++)
                {
                    if (!VisiblePixel(new Point(x, y))) continue;

                    visible = true;
                    break;

                }
                if (!visible) continue;

                t = y;
                break;
            }

            visible = false;
            for (int x = r - 1; x >= l; x--)
            {
                for (int y = 0; y < b; y++)
                {
                    if (!VisiblePixel(new Point(x, y))) continue;

                    visible = true;
                    break;
                }

                if (!visible) continue;

                r = x + 1;
                break;
            }

            visible = false;
            for (int y = b - 1; y >= t; y--)
            {
                for (int x = l; x < r; x++)
                {
                    if (!VisiblePixel(new Point(x, y))) continue;

                    visible = true;
                    break;

                }
                if (!visible) continue;

                b = y + 1;
                break;
            }

            TrueSize = Rectangle.FromLTRB(l, t, r, b).Size;

            return TrueSize;
        }

        private static byte[] DecompressImage(byte[] image)
        {
            using (GZipStream stream = new GZipStream(new MemoryStream(image), CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return memory.ToArray();
                }
            }
        }
    }
}
