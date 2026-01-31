using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoulsFormats;
using SoulsIds;

namespace FogMod
{
	// Token: 0x02000013 RID: 19
	public class GameDataWriterE
	{
		// Token: 0x060000B5 RID: 181 RVA: 0x00011604 File Offset: 0x0000F804
		public void Write(RandomizerOptions opt, AnnotationData ann, Graph g, MergedMods mergedMods, string outDir, Events events, EventConfig eventConfig, Action<string> notify)
		{
			GameDataWriterE.<>c__DisplayClass1_0 CS$<>8__locals1 = new GameDataWriterE.<>c__DisplayClass1_0();
			CS$<>8__locals1.mergedMods = mergedMods;
			CS$<>8__locals1.g = g2;
			CS$<>8__locals1.opt = opt;
			CS$<>8__locals1.events = events;
			CS$<>8__locals1.outDir = outDir;
			CS$<>8__locals1.editor = new GameEditor(9);
			string text = "eldendata\\Vanilla";
			string text2 = "..\\randomizer\\diste\\Vanilla";
			CS$<>8__locals1.editor.Spec.GameDir = ((Directory.Exists(text2) && !Directory.Exists(text)) ? text2 : text);
			CS$<>8__locals1.editor.Spec.DefDir = "eldendata\\Defs";
			CS$<>8__locals1.editor.Spec.NameDir = "eldendata\\Names";
			bool flag = CS$<>8__locals1.g.ExcludeMode != AnnotationData.AreaMode.DLC;
			if (notify != null)
			{
				notify("Reading game data");
			}
			CS$<>8__locals1.msbs = new Dictionary<string, MSBE>();
			if (!CS$<>8__locals1.opt["checkitems"])
			{
				CS$<>8__locals1.msbs = CS$<>8__locals1.<Write>g__loadDir|2<MSBE>(CS$<>8__locals1.editor.Spec.GameDir, "map\\mapstudio", (string path) => SoulsFile<MSBE>.Read(path), "*.msb.dcx").Item1;
			}
			CS$<>8__locals1.emevds = CS$<>8__locals1.<Write>g__loadDir|2<EMEVD>(CS$<>8__locals1.editor.Spec.GameDir, "event", (string path) => SoulsFile<EMEVD>.Read(path), "*.emevd.dcx").Item1;
			CS$<>8__locals1.writeMsbs = new HashSet<string>();
			CS$<>8__locals1.writeEmevds = new HashSet<string>
			{
				"common",
				"common_func"
			};
			if (CS$<>8__locals1.msbs.Count > 0)
			{
				MiscSetup.FixEldenMaps(CS$<>8__locals1.msbs, CS$<>8__locals1.writeMsbs);
			}
			CS$<>8__locals1.lastRe = new Regex("_1([0-2])$");
			CS$<>8__locals1.mapDupes = (from m in GameDataWriterE.dupeMsbs
			where !CS$<>8__locals1.msbs.ContainsKey(m)
			select m).ToDictionary((string m) => CS$<>8__locals1.lastRe.Replace(m, "_0$1"), (string m) => m);
			CS$<>8__locals1.editor.LoadNames<string>("MapName", (string n) => n, false);
			string text3 = CS$<>8__locals1.<Write>g__resolvePath|0(CS$<>8__locals1.editor.Spec.GameDir + "\\msg\\engus\\item_dlc02.msgbnd.dcx", "msg\\engus", false);
			CS$<>8__locals1.itemFMGs = CS$<>8__locals1.<Write>g__read|9(text3);
			string text4 = CS$<>8__locals1.<Write>g__resolvePath|0(CS$<>8__locals1.editor.Spec.GameDir + "\\msg\\engus\\menu_dlc02.msgbnd.dcx", "msg\\engus", false);
			CS$<>8__locals1.menuFMGs = CS$<>8__locals1.<Write>g__read|9(text4);
			string text5 = CS$<>8__locals1.<Write>g__resolvePath|0(CS$<>8__locals1.editor.Spec.GameDir + "\\" + CS$<>8__locals1.editor.Spec.ParamFile, ".", false);
			CS$<>8__locals1.Params = new ParamDictionary
			{
				Defs = CS$<>8__locals1.editor.LoadDefs(),
				Inner = CS$<>8__locals1.editor.LoadParams(text5, null)
			};
			CS$<>8__locals1.coord = new EldenCoordinator(CS$<>8__locals1.Params);
			CS$<>8__locals1.editEsds = new List<string>
			{
				"m00_00_00_00",
				"m11_05_00_00",
				"m61_00_00_00"
			};
			EventEditor.GameData data;
			ValueTuple<Dictionary<string, Dictionary<string, ESD>>, Dictionary<string, string>> valueTuple = CS$<>8__locals1.<Write>g__loadFiles|1<Dictionary<string, ESD>>(from p in Directory.GetFiles(CS$<>8__locals1.editor.Spec.GameDir, "*.talkesdbnd.dcx")
			where CS$<>8__locals1.editEsds.Contains(GameEditor.BaseName(p))
			select p, "script\\talk", (string esdPath) => CS$<>8__locals1.editor.LoadBnd<ESD>(esdPath, (byte[] data, string _) => SoulsFile<ESD>.Read(data), null));
			CS$<>8__locals1.esds = valueTuple.Item1;
			Dictionary<string, string> item = valueTuple.Item2;
			CS$<>8__locals1.copyEsdsFrom = new Dictionary<ValueTuple<string, int>, ValueTuple<string, int>>();
			CS$<>8__locals1.esdSubId = 60;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
			if (CS$<>8__locals1.opt["dumpesd"])
			{
				foreach (string text6 in Directory.GetFiles(CS$<>8__locals1.editor.Spec.GameDir, "*.talkesdbnd.dcx"))
				{
					BND4 bnd = SoulsFile<BND4>.Read(text6);
					Console.WriteLine("-- " + text6);
					foreach (BinderFile binderFile in bnd.Files)
					{
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(7, 4);
						defaultInterpolatedStringHandler.AppendFormatted<int>(binderFile.ID);
						defaultInterpolatedStringHandler.AppendLiteral(" - ");
						defaultInterpolatedStringHandler.AppendFormatted(binderFile.Name);
						defaultInterpolatedStringHandler.AppendLiteral(" - ");
						defaultInterpolatedStringHandler.AppendFormatted<Binder.FileFlags>(binderFile.Flags);
						defaultInterpolatedStringHandler.AppendLiteral(" ");
						defaultInterpolatedStringHandler.AppendFormatted<DCX.Type>(binderFile.CompressionType);
						Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
					}
				}
				return;
			}
			if (notify != null)
			{
				notify("Modifying game data");
			}
			if (CS$<>8__locals1.opt["sombermode"])
			{
				foreach (PARAM.Row row22 in CS$<>8__locals1.Params["EquipMtrlSetParam"].Rows)
				{
					int num = (int)row22["materialId01"].Value;
					int num2 = (int)((byte)row22["materialCate01"].Value);
					int num3 = (int)((sbyte)row22["itemNum01"].Value);
					if (num2 == 4 && num >= 10100 && num < 10110 && num3 > 1)
					{
						row22["itemNum01"].Value = 1;
					}
				}
			}
			uint num4 = 755890000U;
			uint num5 = 1040290000U;
			uint num6 = num5 + 70U;
			uint num7 = num5 + 100U;
			uint num8 = num5 + 191U;
			uint num9 = num5 + 200U;
			CS$<>8__locals1.newStoneBase = num5 + 800U;
			uint value = num5 + 2070U;
			uint num10 = num5 + 2100U;
			uint num11 = num5 + 2300U;
			CS$<>8__locals1.segmentStartedBase = num5 + 4000U;
			uint num12 = num5 + 4800U;
			uint num13 = num5 + 5200U;
			Dictionary<string, List<ValueTuple<string, int>>> dictionary = new Dictionary<string, List<ValueTuple<string, int>>>();
			Dictionary<string, List<uint>> dictionary2 = new Dictionary<string, List<uint>>();
			CS$<>8__locals1.partIndex = 20000;
			if (ann == null)
			{
				ann = new AnnotationData
				{
					Entrances = new List<AnnotationData.Entrance>(),
					Warps = new List<AnnotationData.Entrance>()
				};
			}
			GameDataWriterE.<>c__DisplayClass1_0 CS$<>8__locals2 = CS$<>8__locals1;
			Dictionary<string, string> dictionary3 = new Dictionary<string, string>();
			dictionary3["m60_45_32_00"] = "m60_44_32_00";
			dictionary3["m60_45_40_00"] = "m60_45_39_00";
			dictionary3["m60_46_37_00"] = "m60_45_37_00";
			CS$<>8__locals2.neighborMaps = dictionary3;
			CS$<>8__locals1.overworldAssetBase = CS$<>8__locals1.msbs["m60_46_38_00"].Parts.Assets.Find((MSBE.Part.Asset e) => e.Name == "AEG007_310_2000");
			if (CS$<>8__locals1.overworldAssetBase == null)
			{
				throw new Exception("Can't add new assets, missing base asset (AEG007_310_2000 in m60_46_38_00)");
			}
			if (CS$<>8__locals1.msbs["m60_41_38_00"].Parts.Enemies.Find((MSBE.Part.Enemy e) => e.Name == "c1000_9000") == null)
			{
				throw new Exception("Can't add new enemies, missing base enemy (c1000_9000 in m60_41_38_00)");
			}
			HashSet<string> hashSet = new HashSet<string>();
			foreach (PARAM.Row row2 in CS$<>8__locals1.Params["AssetEnvironmentGeometryParam"].Rows)
			{
				if (!CS$<>8__locals1.opt["multi"])
				{
					break;
				}
				if ((byte)row2["isCreateMultiPlayOnly"].Value > 0)
				{
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(0, 1);
					defaultInterpolatedStringHandler.AppendFormatted<int>(row2.ID, "d6");
					string text7 = defaultInterpolatedStringHandler.ToStringAndClear();
					string text8 = "AEG" + text7.Substring(0, 3) + "_" + text7.Substring(3);
					hashSet.Add(text8);
					Console.WriteLine("Multi model " + text8);
					row2["isCreateMultiPlayOnly"].Value = 0;
				}
			}
			foreach (int num14 in new int[]
			{
				99230,
				99231,
				99232
			})
			{
				CS$<>8__locals1.Params["AssetEnvironmentGeometryParam"][num14]["isCreateMultiPlayOnly"].Value = 0;
			}
			CS$<>8__locals1.mfogModels = new HashSet<string>
			{
				"AEG099_230",
				"AEG099_231",
				"AEG099_232"
			};
			new HashSet<string>
			{
				"AEG099_001",
				"AEG099_002",
				"AEG099_003",
				"AEG099_239"
			}.UnionWith(CS$<>8__locals1.mfogModels);
			CS$<>8__locals1.ownerMap = new Dictionary<int, string>();
			ann.Entrances.ToDictionary((AnnotationData.Entrance e) => e.ID, (AnnotationData.Entrance e) => e);
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(10, 1);
			defaultInterpolatedStringHandler.AppendFormatted<int>(CS$<>8__locals1.g.EntranceIds.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" entrances");
			Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
			using (List<AnnotationData.Entrance>.Enumerator enumerator3 = ann.Entrances.GetEnumerator())
			{
				while (enumerator3.MoveNext())
				{
					AnnotationData.Entrance e = enumerator3.Current;
					if (e.HasTag("remove"))
					{
						string area16 = e.Area;
						CS$<>8__locals1.msbs[area16].Parts.Assets.RemoveAll((MSBE.Part.Asset o) => o.Name == e.Name);
						CS$<>8__locals1.writeMsbs.Add(area16);
					}
					else
					{
						AnnotationData.Entrance src;
						if (e.SplitFrom != null && CS$<>8__locals1.g.EntranceIds.TryGetValue(e.SplitFrom, out src))
						{
							MSBE.Part.Asset asset = CS$<>8__locals1.msbs[src.Area].Parts.Assets.Find((MSBE.Part.Asset o) => o.Name == src.Name);
							if (asset == null)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 3);
								defaultInterpolatedStringHandler.AppendLiteral("Asset ");
								defaultInterpolatedStringHandler.AppendFormatted(src.Name);
								defaultInterpolatedStringHandler.AppendLiteral(" not found in map ");
								defaultInterpolatedStringHandler.AppendFormatted(src.Area);
								defaultInterpolatedStringHandler.AppendLiteral(", needed for ");
								defaultInterpolatedStringHandler.AppendFormatted(e.FullName);
								throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
							}
							MSBE.Part.Asset asset2 = (MSBE.Part.Asset)asset.DeepCopy();
							if (!GameDataWriterE.<Write>g__setAssetName|1_24(asset2, e.Name))
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(75, 3);
								defaultInterpolatedStringHandler.AppendLiteral("Can't transfer ");
								defaultInterpolatedStringHandler.AppendFormatted(src.FullName);
								defaultInterpolatedStringHandler.AppendLiteral(" to ");
								defaultInterpolatedStringHandler.AppendFormatted(e.FullName);
								defaultInterpolatedStringHandler.AppendLiteral(", since it references map object which may not exist in ");
								defaultInterpolatedStringHandler.AppendFormatted(e.Area);
								throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
							}
							asset2.EntityID = (uint)e.ID;
							MSBE msbe = CS$<>8__locals1.msbs[e.Area];
							msbe.Parts.Assets.Add(asset2);
							GameDataWriterE.<Write>g__addAssetModel|1_21(msbe, asset2.ModelName);
						}
						if (!e.HasTag("unused"))
						{
							if (e.MakeFrom != null)
							{
								string[] array3 = e.MakeFrom.Split(' ', StringSplitOptions.None);
								List<float> list = GameDataWriterE.<Write>g__parseFloats|1_20(array3.Skip(2));
								Vector3 pos = new Vector3(list[0], list[1], list[2]);
								Vector3 rot = new Vector3(0f, list[3], 0f);
								MSBE.Part.Asset asset3 = CS$<>8__locals1.<Write>g__addFakeGate|25(e.Area, array3[0], array3[1], pos, rot, e.Name);
								asset3.EntityID = (uint)e.ID;
								if (CS$<>8__locals1.mfogModels.Contains(asset3.ModelName))
								{
									asset3.AssetSfxParamRelativeID = 0;
								}
							}
							if (!e.HasTag("norandom") && !e.HasTag("door"))
							{
								Predicate<MSBE.Part.Asset> <>9__99;
								for (int j = 0; j <= 1; j++)
								{
									bool flag2 = j == 0;
									AnnotationData.Side side11 = flag2 ? e.ASide : e.BSide;
									if (side11 != null)
									{
										string area2 = e.Area;
										List<MSBE.Part.Asset> assets2 = CS$<>8__locals1.msbs[area2].Parts.Assets;
										Predicate<MSBE.Part.Asset> match;
										if ((match = <>9__99) == null)
										{
											match = (<>9__99 = ((MSBE.Part.Asset o) => o.Name == e.Name));
										}
										MSBE.Part.Asset asset4 = assets2.Find(match);
										if (asset4 == null)
										{
											throw new Exception("Asset " + e.Name + " not found in map " + e.Area);
										}
										if ((ulong)asset4.EntityID != (ulong)((long)e.ID))
										{
											if (asset4.EntityID != 0U || e.ID / 1000 != 755894)
											{
												defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(53, 3);
												defaultInterpolatedStringHandler.AppendLiteral("Asset ");
												defaultInterpolatedStringHandler.AppendFormatted(e.FullName);
												defaultInterpolatedStringHandler.AppendLiteral(" expected to be assigned id ");
												defaultInterpolatedStringHandler.AppendFormatted<int>(e.ID);
												defaultInterpolatedStringHandler.AppendLiteral(", but it's already ");
												defaultInterpolatedStringHandler.AppendFormatted<uint>(asset4.EntityID);
												throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
											}
											asset4.EntityID = (uint)e.ID;
										}
										string text9 = CS$<>8__locals1.<Write>g__getEventMap|14(area2, asset4.Name);
										if (e.ID > 0)
										{
											CS$<>8__locals1.ownerMap[e.ID] = text9;
										}
										uint entityID = asset4.EntityID;
										if (e.FullName == "m11_00_00_00_AEG099_231_9000" || e.SplitFrom == "m11_00_00_00_AEG099_231_9000")
										{
											asset4.Position = new Vector3(105.877f, -21.949f, -200.037f);
										}
										else if (e.FullName == "m61_49_48_00_AEG099_230_9000")
										{
											asset4.Position = new Vector3(99.257f, 577.232f, -85.016f);
											asset4.Rotation = new Vector3(0f, -59.901f, 0f);
										}
										if (e.Strafe != 0f)
										{
											asset4.Position += GameDataWriterE.<Write>g__getStrafeOffset|1_19(asset4.Rotation, e.Strafe);
											e.Strafe = 0f;
										}
										if (e.Raise != 0f)
										{
											asset4.Position += new Vector3(0f, e.Raise, 0f);
											asset4.Rotation = new Vector3(0f, asset4.Rotation.Y, 0f);
											e.Raise = 0f;
										}
										if (e.Extend != null && !e.IsFixed)
										{
											if (asset4.ModelName != "AEG099_231")
											{
												defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(42, 2);
												defaultInterpolatedStringHandler.AppendLiteral("Internal error ");
												defaultInterpolatedStringHandler.AppendFormatted(e.FullName);
												defaultInterpolatedStringHandler.AppendLiteral(": ");
												defaultInterpolatedStringHandler.AppendFormatted(asset4.ModelName);
												defaultInterpolatedStringHandler.AppendLiteral(" not supported for Extend");
												throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
											}
											List<float> list2 = GameDataWriterE.<Write>g__parseFloats|1_20(e.Extend.Split(' ', StringSplitOptions.None));
											Vector3 position = asset4.Position;
											Vector3 rotation = asset4.Rotation;
											float num15 = rotation.Y * 3.1415927f / 180f - 1.5707964f;
											Vector3 left = new Vector3((float)Math.Sin((double)num15), 0f, (float)Math.Cos((double)num15));
											float num16 = list2[0];
											float num17 = list2[1];
											bool[] array4 = new bool[2];
											array4[0] = true;
											foreach (bool flag3 in array4)
											{
												float num18 = (float)(flag3 ? 15 : 12);
												float num19 = (float)(flag3 ? 15 : 14);
												int num20 = Math.Max(0, (int)Math.Ceiling((double)(num16 / num18) - 0.5));
												int num21 = Math.Max(0, (int)Math.Ceiling((double)(num17 / num18) - 0.5));
												for (int k = -num20; k <= num21; k++)
												{
													Vector3 vector = position + left * (float)k * num18;
													if (k != 0)
													{
														CS$<>8__locals1.<Write>g__addFakeGate|25(area2, "AEG099_231", asset4.Name, vector, rotation, null).AssetSfxParamRelativeID = (flag3 ? 0 : -1);
													}
													if (list2.Count >= 3 && list2[2] >= num19)
													{
														CS$<>8__locals1.<Write>g__addFakeGate|25(area2, "AEG099_231", asset4.Name, vector + new Vector3(0f, num19, 0f), rotation, null).AssetSfxParamRelativeID = (flag3 ? 0 : -1);
													}
												}
											}
										}
										Vector3 position2 = asset4.Position;
										float dist = 1f;
										float num22 = -1f;
										if (side11.HasTag("main"))
										{
											num22 = 1f;
											if (side11.HasTag("unstable"))
											{
												num22 = 2f;
											}
										}
										Vector3 vector2 = GameDataWriterE.<Write>g__oppositeRotation|1_30(asset4.Rotation);
										uint entityID2;
										if (!flag2 && !e.IsFixed)
										{
											MSBE.Part.Asset asset5 = (MSBE.Part.Asset)asset4.DeepCopy();
											GameDataWriterE.<Write>g__setAssetName|1_24(asset5, CS$<>8__locals1.<Write>g__newPartName|12(area2, asset4.ModelName, asset4.Name));
											asset5.Rotation = vector2;
											asset5.EntityID = num4++;
											if (CS$<>8__locals1.mfogModels.Contains(asset5.ModelName))
											{
												asset5.AssetSfxParamRelativeID = -1;
											}
											if (!(area2 == "m60_38_51_00"))
											{
												area2 == "m60_39_51_00";
											}
											entityID2 = asset5.EntityID;
											CS$<>8__locals1.msbs[area2].Parts.Assets.Add(asset5);
											CS$<>8__locals1.writeMsbs.Add(area2);
										}
										else
										{
											entityID2 = asset4.EntityID;
										}
										MSBE.Region region = null;
										uint num23 = num4++;
										defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(2, 3);
										defaultInterpolatedStringHandler.AppendFormatted(flag2 ? "front" : "back");
										defaultInterpolatedStringHandler.AppendLiteral(" ");
										defaultInterpolatedStringHandler.AppendFormatted(e.Name);
										defaultInterpolatedStringHandler.AppendLiteral(" ");
										defaultInterpolatedStringHandler.AppendFormatted<uint>(num23);
										string text10 = defaultInterpolatedStringHandler.ToStringAndClear();
										string text11;
										MSBE.Region region2;
										if (side11.CustomWarp == null)
										{
											Vector3 rotation2;
											Vector3 r2;
											if (!flag2)
											{
												rotation2 = vector2;
												r2 = asset4.Rotation;
											}
											else
											{
												rotation2 = asset4.Rotation;
												r2 = vector2;
											}
											Vector3 vector3 = GameDataWriterE.<Write>g__moveInDirection|1_29(position2, r2, dist);
											if (e.AdjustHeight != 0f)
											{
												vector3 = new Vector3(vector3.X, vector3.Y + e.AdjustHeight, vector3.Z);
											}
											if (side11.AdjustHeight != 0f)
											{
												vector3 = new Vector3(vector3.X, vector3.Y + side11.AdjustHeight, vector3.Z);
											}
											text11 = (side11.DestinationMap ?? text9);
											if (text11 != area2)
											{
												vector3 += CS$<>8__locals1.<Write>g__getMapOffset|28(area2, text11);
											}
											if (!text11.EndsWith("0"))
											{
												defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 2);
												defaultInterpolatedStringHandler.AppendLiteral("Invalid warp target area ");
												defaultInterpolatedStringHandler.AppendFormatted(text11);
												defaultInterpolatedStringHandler.AppendLiteral(" for ");
												defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Entrance>(e);
												defaultInterpolatedStringHandler.AppendLiteral(", would softlock game");
												throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
											}
											region2 = new MSBE.Region.SpawnPoint();
											region2.Name = text10;
											region2.EntityID = num23;
											region2.Position = vector3;
											region2.Rotation = rotation2;
											CS$<>8__locals1.msbs[text11].Regions.Add(region2);
											side11.Warp = new Graph.WarpPoint
											{
												ID = e.ID,
												Map = text11,
												Position = new Vector3?(vector3),
												Action = (int)entityID2,
												Region = (int)num23
											};
											if (num22 > 0f)
											{
												region = region2.DeepCopy();
												region.Name = "retry " + text10;
												region.EntityID = num4++;
												region.Position = GameDataWriterE.<Write>g__moveInDirection|1_29(region.Position, r2, num22);
												CS$<>8__locals1.msbs[text11].Regions.Add(region);
												side11.Warp.Retry = (int)region.EntityID;
											}
										}
										else
										{
											string[] array6 = side11.CustomWarp.Split(' ', StringSplitOptions.None);
											text11 = array6[0];
											MSBE msbe2 = CS$<>8__locals1.msbs[text11];
											List<float> list3 = GameDataWriterE.<Write>g__parseFloats|1_20(array6.Skip(1));
											region2 = new MSBE.Region.SpawnPoint();
											region2.Name = text10;
											region2.EntityID = num23;
											region2.Position = new Vector3(list3[0], list3[1], list3[2]);
											region2.Rotation = new Vector3(0f, list3[3], 0f);
											msbe2.Regions.Add(region2);
											side11.Warp = new Graph.WarpPoint
											{
												ID = e.ID,
												Map = text11,
												Position = new Vector3?(region2.Position),
												Action = (int)entityID2,
												Region = (int)num23
											};
										}
										if (side11.BossTriggerName != null)
										{
											string text12 = (side11.BossTriggerName == "area") ? side11.Area : side11.BossTriggerName;
											MSBE.Region region3 = new MSBE.Region.Other();
											region3.Name = text12 + " " + region2.Name;
											region3.EntityID = num4++;
											if (side11.AltBossTriggerArea == null)
											{
												Vector3 vector4 = region2.Position;
												float num24 = 0f;
												if (region != null)
												{
													num24 = Vector3.Distance(region2.Position, region.Position);
													vector4 = Vector3.Lerp(region2.Position, region.Position, 0.5f);
												}
												region3.Position = new Vector3(vector4.X, vector4.Y - 1f, vector4.Z);
												region3.Rotation = region2.Rotation;
												region3.Shape = new MSB.Shape.Box
												{
													Width = 1.5f,
													Depth = 1.5f + num24,
													Height = 4f
												};
											}
											else
											{
												GameDataWriterE.<Write>g__setBoxRegion|1_27(region3, side11.AltBossTriggerArea);
											}
											CS$<>8__locals1.msbs[text11].Regions.Add(region3);
											Util.AddMulti<string, ValueTuple<string, int>>(dictionary, text12, new ValueTuple<string, int>(text11, (int)region3.EntityID));
											if (side11.BossTriggerArea != null)
											{
												region3 = new MSBE.Region.Other();
												region3.Name = text12 + " " + region2.Name + " other";
												region3.EntityID = num4++;
												GameDataWriterE.<Write>g__setBoxRegion|1_27(region3, side11.BossTriggerArea);
												CS$<>8__locals1.msbs[text11].Regions.Add(region3);
												Util.AddMulti<string, ValueTuple<string, int>>(dictionary, text12, new ValueTuple<string, int>(text11, (int)region3.EntityID));
											}
										}
										CS$<>8__locals1.writeMsbs.Add(text11);
									}
								}
								AnnotationData.Side aside = e.ASide;
								if (((aside != null) ? aside.Warp : null) != null)
								{
									AnnotationData.Side bside = e.BSide;
									if (((bside != null) ? bside.Warp : null) != null)
									{
										e.ASide.Warp.OtherSide = e.BSide.Warp.Region;
										e.BSide.Warp.OtherSide = e.ASide.Warp.Region;
									}
								}
								if (e.DoorName != null)
								{
									string[] array7 = e.DoorName.Split(' ', StringSplitOptions.None);
									string doorName = array7[0];
									string text13 = (array7.Length > 1) ? array7[1] : e.Area;
									MSBE.Part.Asset asset6 = CS$<>8__locals1.msbs[text13].Parts.Assets.Find((MSBE.Part.Asset o) => o.Name == doorName);
									if (asset6 != null)
									{
										MSBE.Event.ObjAct objAct = CS$<>8__locals1.msbs[text13].Events.ObjActs.Find((MSBE.Event.ObjAct oa) => oa.ObjActPartName == doorName);
										if (objAct == null)
										{
											CS$<>8__locals1.msbs[text13].Parts.Assets.Remove(asset6);
										}
										else if (objAct.EventFlagID > 0U && !e.HasTag("cellar"))
										{
											Util.AddMulti<string, uint>(dictionary2, CS$<>8__locals1.<Write>g__getEventMap|14(text13, asset6.Name), objAct.EventFlagID);
										}
										else
										{
											CS$<>8__locals1.msbs[text13].Parts.Assets.Remove(asset6);
											CS$<>8__locals1.msbs[text13].Events.ObjActs.Remove(objAct);
										}
									}
								}
							}
						}
					}
				}
			}
			CS$<>8__locals1.ownerMap[1052531802] = "m60_52_53_00";
			HashSet<string> hashSet2 = new HashSet<string>();
			foreach (AnnotationData.Entrance entrance in ann.Entrances.Concat(ann.Warps))
			{
				for (int l2 = 0; l2 <= 1; l2++)
				{
					bool flag4 = l2 == 0;
					AnnotationData.Side side2 = flag4 ? entrance.ASide : entrance.BSide;
					if (side2 != null && side2.AlternateOf != null)
					{
						string[] array8 = side2.AlternateOf.Split(' ', StringSplitOptions.None);
						AnnotationData.Entrance entrance2;
						if (!CS$<>8__locals1.g.EntranceIds.TryGetValue(array8[0], out entrance2))
						{
							throw new Exception("Unknown alternate entrance " + entrance.FullName + "->" + side2.AlternateOf);
						}
						AnnotationData.Side side3 = flag4 ? entrance2.ASide : entrance2.BSide;
						if (array8.Length == 2 && side2.Warp != null && side3.Warp != null)
						{
							int alternateFlag = int.Parse(array8[1]);
							side3.AlternateSide = side2;
							side3.AlternateFlag = alternateFlag;
						}
						hashSet2.Add(entrance.Name);
					}
				}
			}
			MSBE.Region.Other other = CS$<>8__locals1.msbs["m60_49_53_00"].Regions.Others.Find((MSBE.Region.Other r) => r.EntityID == 1049532506U);
			if (other != null)
			{
				other.Position = new Vector3(-24.281f, 1070.517f, -16.452f);
				other.Rotation = new Vector3(0f, -31.023f, 0f);
			}
			Dictionary<int, ValueTuple<string, int>> dictionary4 = new Dictionary<int, ValueTuple<string, int>>();
			Dictionary<int, ValueTuple<string, int>> dictionary5 = new Dictionary<int, ValueTuple<string, int>>();
			List<AnnotationData.Entrance> list4 = new List<AnnotationData.Entrance>();
			List<AnnotationData.Entrance> list5 = new List<AnnotationData.Entrance>();
			List<AnnotationData.Entrance> list6 = new List<AnnotationData.Entrance>();
			using (List<AnnotationData.Entrance>.Enumerator enumerator3 = ann.Warps.GetEnumerator())
			{
				while (enumerator3.MoveNext())
				{
					AnnotationData.Entrance e = enumerator3.Current;
					if (!e.HasTag("unused"))
					{
						MSBE.Region region4 = null;
						bool flag5 = false;
						if (!e.ASide.HasTag("unused"))
						{
							Vector3? vector5 = null;
							Vector3? vector6 = null;
							if (e.MakeFrom != null)
							{
								string[] array9 = e.MakeFrom.Split(' ', StringSplitOptions.None);
								List<float> list7 = GameDataWriterE.<Write>g__parseFloats|1_20(array9.Skip(2));
								Vector3 vector7 = new Vector3(list7[0], list7[1], list7[2]);
								Vector3 vector8 = (list7.Count == 6) ? new Vector3(list7[4], list7[3], list7[5]) : new Vector3(0f, list7[3], 0f);
								MSBE.Part.Asset asset7 = CS$<>8__locals1.<Write>g__addFakeGate|25(e.Area, array9[0], array9[1], vector7, vector8, null);
								asset7.EntityID = num4++;
								vector5 = new Vector3?(vector7);
								if (asset7.ModelName.StartsWith("AEG099_17"))
								{
									Vector3 position3 = vector7 + new Vector3(0f, 0.16f, 0f);
									region4 = new MSBE.Region.Other
									{
										Name = asset7.Name + " return",
										Position = position3,
										Rotation = vector8
									};
									dictionary5[e.ID] = new ValueTuple<string, int>(e.Area, (int)asset7.EntityID);
								}
								else
								{
									Vector3 position4 = GameDataWriterE.<Write>g__moveInDirection|1_29(vector7, vector8, 2f) + new Vector3(0f, 0.25f, 0f);
									region4 = new MSBE.Region.Other
									{
										Name = asset7.Name + " return",
										Position = position4,
										Rotation = GameDataWriterE.<Write>g__oppositeRotation|1_30(vector8)
									};
									dictionary4[e.ID] = new ValueTuple<string, int>(e.Area, (int)asset7.EntityID);
								}
								if (e.RemoveDest != null)
								{
									HashSet<string> removes = new HashSet<string>(e.RemoveDest.Split(' ', StringSplitOptions.None));
									CS$<>8__locals1.msbs[e.Area].Parts.Assets.RemoveAll((MSBE.Part.Asset o) => removes.Contains(o.Name));
								}
							}
							else if (e.Location > 0)
							{
								Vector3 value2;
								Vector3 value3;
								if (CS$<>8__locals1.<Write>g__getAssetLocation|31(e.Area, e.Location, out value2, out value3))
								{
									vector5 = new Vector3?(value2);
									vector6 = new Vector3?(value3);
								}
								else
								{
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(42, 2);
									defaultInterpolatedStringHandler.AppendLiteral("Asset not found for warp destination: ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(e.Location);
									defaultInterpolatedStringHandler.AppendLiteral(" in ");
									defaultInterpolatedStringHandler.AppendFormatted(e.Area);
									Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
								}
							}
							if (e.HasTag("backportal") && e.HasTag("selfwarp"))
							{
								if (vector5 != null)
								{
									Vector3 valueOrDefault = vector5.GetValueOrDefault();
									if (vector6 != null)
									{
										Vector3 vector9 = vector6.GetValueOrDefault();
										if (e.ASide.CustomWarp != null)
										{
											string[] array10 = e.ASide.CustomWarp.Split(' ', StringSplitOptions.None);
											string text14 = array10[0];
											if (text14 != e.Area)
											{
												throw new Exception("No handling for CustomWarp with map " + text14 + " mismatching entrance map " + e.Area);
											}
											List<float> list8 = GameDataWriterE.<Write>g__parseFloats|1_20(array10.Skip(1));
											valueOrDefault = new Vector3(list8[0], list8[1], list8[2]);
											vector9 = new Vector3(0f, list8[3], 0f);
										}
										else
										{
											vector9 = GameDataWriterE.<Write>g__oppositeRotation|1_30(vector9);
										}
										MSBE.Region.Other other2 = new MSBE.Region.Other();
										defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(7, 1);
										defaultInterpolatedStringHandler.AppendFormatted<int>(e.Location);
										defaultInterpolatedStringHandler.AppendLiteral(" return");
										other2.Name = defaultInterpolatedStringHandler.ToStringAndClear();
										other2.Position = valueOrDefault;
										other2.Rotation = vector9;
										region4 = other2;
										flag5 = true;
										AnnotationData.Area area3;
										if (!CS$<>8__locals1.g.Areas.TryGetValue(e.ASide.Area, out area3))
										{
											throw new Exception("Invalid area " + area3.Name + " in return-to-entrance warp");
										}
										if (area3.DefeatFlag > 0 && area3.BossTrigger > 0)
										{
											MSBE.Region region5 = new MSBE.Region.Other();
											MSBE.Entry entry5 = region5;
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(9, 1);
											defaultInterpolatedStringHandler.AppendFormatted<int>(e.Location);
											defaultInterpolatedStringHandler.AppendLiteral(" entrance");
											entry5.Name = defaultInterpolatedStringHandler.ToStringAndClear();
											region5.EntityID = num4++;
											region5.Shape = new MSB.Shape.Cylinder
											{
												Height = 3f,
												Radius = 2f
											};
											region5.Position = valueOrDefault - new Vector3(0f, 1f, 0f);
											CS$<>8__locals1.msbs[e.Area].Regions.Add(region5);
											Util.AddMulti<string, ValueTuple<string, int>>(dictionary, area3.Name, new ValueTuple<string, int>(e.Area, (int)region5.EntityID));
											goto IL_2336;
										}
										if (!e.HasTag("alwaysback"))
										{
											throw new Exception("Internal error: return-to-entrance warp in " + area3.Name + " has no boss info");
										}
										goto IL_2336;
									}
								}
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(77, 2);
								defaultInterpolatedStringHandler.AppendLiteral("Missing portal ");
								defaultInterpolatedStringHandler.AppendFormatted<int>(e.Location);
								defaultInterpolatedStringHandler.AppendLiteral(" in ");
								defaultInterpolatedStringHandler.AppendFormatted(e.Area);
								defaultInterpolatedStringHandler.AppendLiteral(" missing, needed to create extra return-from-entrance warp");
								throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
							}
							IL_2336:
							if (e.ASide.DestinationMap != null && e.ASide.DestinationMap != e.Area)
							{
								Console.WriteLine("Area mismatch for " + e.FullName);
							}
							e.ASide.Warp = new Graph.WarpPoint
							{
								ID = e.ID,
								Map = (e.ASide.DestinationMap ?? e.Area),
								Position = vector5
							};
							if (e.ASide.WarpBonfire > 0 || e.ASide.WarpDefeatFlag > 0)
							{
								if (CS$<>8__locals1.opt[Feature.NoBossBonfire] || e.ASide.WarpBonfire <= 0)
								{
									list5.Add(e);
								}
								if (e.ASide.WarpBonfire > 0 && !e.HasTag("backportal"))
								{
									e.ASide.Warp.SitFlag = (int)num11++;
									e.ASide.Warp.WarpFlag = (int)num11++;
									list4.Add(e);
								}
							}
							else if (e.HasTag("chestwarp"))
							{
								list6.Add(e);
							}
							else if (vector5 == null)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(26, 4);
								defaultInterpolatedStringHandler.AppendLiteral("Unknown location for ");
								defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Entrance>(e);
								defaultInterpolatedStringHandler.AppendLiteral(" ");
								defaultInterpolatedStringHandler.AppendFormatted(e.Tags);
								defaultInterpolatedStringHandler.AppendLiteral(" - ");
								defaultInterpolatedStringHandler.AppendFormatted(e.Area);
								defaultInterpolatedStringHandler.AppendLiteral(" ");
								defaultInterpolatedStringHandler.AppendFormatted<Vector3?>(vector5);
								Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
							}
						}
						if (!e.BSide.HasTag("unused"))
						{
							string text15 = e.BSide.DestinationMap ?? e.Area;
							MSBE.Region.SpawnPoint spawnPoint = flag5 ? null : CS$<>8__locals1.msbs[text15].Regions.SpawnPoints.Find((MSBE.Region.SpawnPoint r) => (ulong)r.EntityID == (ulong)((long)e.ID));
							if (spawnPoint == null)
							{
								spawnPoint = new MSBE.Region.SpawnPoint();
								spawnPoint.EntityID = num4++;
								if (region4 == null)
								{
									region4 = (flag5 ? null : (from r in CS$<>8__locals1.msbs[text15].Regions.GetEntries()
									where (ulong)r.EntityID == (ulong)((long)e.ID)
									select r).FirstOrDefault<MSBE.Region>());
								}
								if (region4 != null)
								{
									spawnPoint.Name = "SpawnPoint " + region4.Name;
									spawnPoint.Position = region4.Position;
									spawnPoint.Rotation = region4.Rotation;
								}
								else if (e.BSide.CustomWarp != null)
								{
									string[] array11 = e.BSide.CustomWarp.Split(' ', StringSplitOptions.None);
									string text16 = array11[0];
									if (text16 != text15)
									{
										throw new Exception("No handling for CustomWarp with map " + text16 + " mismatching entrance map " + text15);
									}
									List<float> list9 = GameDataWriterE.<Write>g__parseFloats|1_20(array11.Skip(1));
									MSBE.Entry entry2 = spawnPoint;
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(11, 1);
									defaultInterpolatedStringHandler.AppendLiteral("SpawnPoint ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(e.ID);
									entry2.Name = defaultInterpolatedStringHandler.ToStringAndClear();
									spawnPoint.Position = new Vector3(list9[0], list9[1], list9[2]);
									spawnPoint.Rotation = new Vector3(0f, list9[3], 0f);
								}
								else
								{
									MSBE.Part.Player player = flag5 ? null : (from r in CS$<>8__locals1.msbs[text15].Parts.Players
									where (ulong)r.EntityID == (ulong)((long)e.ID)
									select r).FirstOrDefault<MSBE.Part.Player>();
									if (player != null)
									{
										spawnPoint.Name = "SpawnPoint " + player.Name;
										spawnPoint.Position = player.Position;
										spawnPoint.Rotation = player.Rotation;
									}
									else
									{
										if (!(e.BSide.DestinationStake == "area"))
										{
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(29, 2);
											defaultInterpolatedStringHandler.AppendFormatted<int>(e.ID);
											defaultInterpolatedStringHandler.AppendLiteral(" not found as warp target in ");
											defaultInterpolatedStringHandler.AppendFormatted(text15);
											throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
										}
										if (e.BSide.StakeRespawn == null)
										{
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(79, 1);
											defaultInterpolatedStringHandler.AppendLiteral("Internal error: ");
											defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Entrance>(e);
											defaultInterpolatedStringHandler.AppendLiteral(" has area-level DestinationStake but no StakeRespawn is defined");
											throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
										}
										MSBE.Entry entry3 = spawnPoint;
										defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(11, 1);
										defaultInterpolatedStringHandler.AppendLiteral("SpawnPoint ");
										defaultInterpolatedStringHandler.AppendFormatted<uint>(spawnPoint.EntityID);
										entry3.Name = defaultInterpolatedStringHandler.ToStringAndClear();
									}
									if (e.BSide.DestinationStake != null && e.BSide.StakeRespawn != null)
									{
										List<float> list10 = GameDataWriterE.<Write>g__parseFloats|1_20(e.BSide.StakeRespawn.Split(' ', StringSplitOptions.None));
										spawnPoint.Position = new Vector3(list10[0], list10[1], list10[2]);
										spawnPoint.Rotation = new Vector3(0f, list10[3], 0f);
									}
								}
								CS$<>8__locals1.msbs[text15].Regions.Add(spawnPoint);
								CS$<>8__locals1.writeMsbs.Add(text15);
							}
							e.BSide.Warp = new Graph.WarpPoint
							{
								ID = e.ID,
								Map = text15,
								Position = new Vector3?(spawnPoint.Position),
								Region = (int)spawnPoint.EntityID
							};
						}
					}
				}
			}
			GameDataWriterE.<>c__DisplayClass1_0 CS$<>8__locals8 = CS$<>8__locals1;
			Dictionary<string, string> dictionary6 = new Dictionary<string, string>();
			dictionary6["dragonbarrow_cave_boss"] = "dragonbarrow_cave_preboss";
			CS$<>8__locals8.mainEntranceOverrides = dictionary6;
			bool flag6 = CS$<>8__locals1.opt[Feature.AllowUnlinked];
			Dictionary<string, AnnotationData.Side> dictionary7 = new Dictionary<string, AnnotationData.Side>();
			Dictionary<string, string> dictionary8 = new Dictionary<string, string>();
			foreach (Graph.Node node in CS$<>8__locals1.g.Nodes.Values)
			{
				foreach (Graph.Edge edge in node.To)
				{
					AnnotationData.Entrance entrance3 = (edge.Name == null) ? null : CS$<>8__locals1.g.EntranceIds[edge.Name];
					Graph.Edge link = edge.Link;
					if (link == null)
					{
						if (!flag6)
						{
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(25, 1);
							defaultInterpolatedStringHandler.AppendLiteral("Internal error: Unlinked ");
							defaultInterpolatedStringHandler.AppendFormatted<Graph.Edge>(edge);
							throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
						}
					}
					else
					{
						Graph.WarpPoint warp = edge.Side.Warp;
						Graph.WarpPoint warp2 = link.Side.Warp;
						if (warp == null || warp2 == null)
						{
							if (!edge.IsFixed || !link.IsFixed)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(26, 4);
								defaultInterpolatedStringHandler.AppendLiteral("Missing warps - ");
								defaultInterpolatedStringHandler.AppendFormatted<bool>(warp == null);
								defaultInterpolatedStringHandler.AppendLiteral(" ");
								defaultInterpolatedStringHandler.AppendFormatted<bool>(warp2 == null);
								defaultInterpolatedStringHandler.AppendLiteral(" for ");
								defaultInterpolatedStringHandler.AppendFormatted<Graph.Edge>(edge);
								defaultInterpolatedStringHandler.AppendLiteral(" -> ");
								defaultInterpolatedStringHandler.AppendFormatted<Graph.Edge>(link);
								throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
							}
						}
						else if (warp.Action == 0 && (!(edge.Name == link.Name) || !edge.IsFixed || CS$<>8__locals1.opt["alwaysshow"]))
						{
							string name = entrance3.Name;
							dictionary7[name] = link.Side;
							if (edge.Side.ReturnWarp != null)
							{
								dictionary8[edge.Side.ReturnWarp] = name;
								if (edge.Name == link.Name && edge.Pair != null && edge.Pair.Side.BeforeWarpFlag != 0)
								{
									link.Side.BeforeWarpFlag = -edge.Pair.Side.BeforeWarpFlag;
								}
							}
						}
					}
				}
			}
			Dictionary<int, List<ValueTuple<string, int>>> dictionary9 = new Dictionary<int, List<ValueTuple<string, int>>>();
			int i2;
			if ((CS$<>8__locals1.opt["crawl"] || CS$<>8__locals1.opt["collect"]) && ann.DungeonItems != null)
			{
				GameDataWriterE.<>c__DisplayClass1_11 CS$<>8__locals9 = new GameDataWriterE.<>c__DisplayClass1_11();
				CS$<>8__locals9.CS$<>8__locals2 = CS$<>8__locals1;
				Dictionary<string, HashSet<string>> dictionary10 = new Dictionary<string, HashSet<string>>();
				dictionary10["seedtree"] = new HashSet<string>
				{
					"AEG099_135",
					"AEG099_145",
					"AEG007_291",
					"AEG099_136",
					"AEG099_138",
					"AEG099_146"
				};
				dictionary10["church"] = new HashSet<string>
				{
					"AEG007_310",
					"AEG007_311",
					"AEG007_312",
					"AEG099_136",
					"AEG099_138"
				};
				dictionary10["cross"] = new HashSet<string>
				{
					"AEG464_900",
					"AEG464_906"
				};
				dictionary10["academykey"] = new HashSet<string>();
				dictionary10["fragment"] = new HashSet<string>
				{
					"AEG050_740",
					"AEG050_742",
					"AEG052_688",
					"AEG070_522"
				};
				dictionary10["revered"] = new HashSet<string>
				{
					"AEG052_786",
					"AEG052_787",
					"AEG050_267",
					"AEG052_788",
					"AEG052_789"
				};
				dictionary10["hangingbell"] = new HashSet<string>
				{
					"AEG052_400"
				};
				dictionary10["omother"] = new HashSet<string>
				{
					"AEG053_134",
					"AEG050_610"
				};
				dictionary10["raceshop"] = new HashSet<string>
				{
					"AEG700_043",
					"AEG700_053",
					"AEG201_027",
					"AEG201_043",
					"AEG003_061",
					"c3210",
					"AEG003_362",
					"AEG003_363",
					"AEG003_364",
					"AEG003_071",
					"AEG099_321",
					"AEG099_322",
					"c4604"
				};
				dictionary10["mooreshop"] = new HashSet<string>
				{
					"AEG464_050",
					"AEG052_123",
					"AEG052_129"
				};
				dictionary10["npc"] = new HashSet<string>();
				Dictionary<string, HashSet<string>> dictionary11 = dictionary10;
				CS$<>8__locals9.fragmentStatues = new HashSet<string>
				{
					"AEG050_740",
					"AEG050_742",
					"AEG052_688"
				};
				Dictionary<string, HashSet<string>> dictionary12 = new Dictionary<string, HashSet<string>>();
				dictionary12["noextra"] = new HashSet<string>
				{
					"AEG201_027",
					"AEG201_043",
					"AEG003_061"
				};
				dictionary12["noshack"] = new HashSet<string>
				{
					"AEG052_789"
				};
				dictionary12["nopedestal"] = new HashSet<string>
				{
					"AEG050_267",
					"AEG052_788"
				};
				dictionary12["nolantern"] = new HashSet<string>
				{
					"AEG070_522"
				};
				Dictionary<string, HashSet<string>> dictionary13 = dictionary12;
				MSBE.Region.Message message = CS$<>8__locals9.CS$<>8__locals2.msbs["m18_00_00_00"].Regions.Messages.FirstOrDefault<MSBE.Region.Message>();
				if (message == null)
				{
					throw new Exception("Can't move items to dungeons, missing Message region in m18");
				}
				CS$<>8__locals9.baseBloodMsgId = 9700;
				Vector3 right = new Vector3(0f, 24.3f, 0f);
				using (List<AnnotationData.DungeonItem>.Enumerator enumerator7 = ann.DungeonItems.GetEnumerator())
				{
					while (enumerator7.MoveNext())
					{
						AnnotationData.DungeonItem d = enumerator7.Current;
						GameDataWriterE.<>c__DisplayClass1_13 CS$<>8__locals11 = new GameDataWriterE.<>c__DisplayClass1_13();
						if (!d.IsExcluded)
						{
							if (d.ToMap == null)
							{
								throw new NotImplementedException();
							}
							if (CS$<>8__locals9.CS$<>8__locals2.opt["debugmove"])
							{
								Console.WriteLine(d.Text);
							}
							string text17 = (from t in dictionary11.Keys
							where d.HasTag(t)
							select t).FirstOrDefault<string>();
							if (text17 == null)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 2);
								defaultInterpolatedStringHandler.AppendLiteral("Internal error: item relocation ");
								defaultInterpolatedStringHandler.AppendFormatted(d.Map);
								defaultInterpolatedStringHandler.AppendLiteral("->");
								defaultInterpolatedStringHandler.AppendFormatted(d.ToMap);
								defaultInterpolatedStringHandler.AppendLiteral(" is missing type");
								throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
							}
							bool flag7 = d.ToMap.StartsWith("m6");
							List<string> list11 = (d.Base == null) ? new List<string>() : d.Base.Split(' ', StringSplitOptions.None).ToList<string>();
							int.TryParse((list11.Count > 0) ? list11[0] : null, out CS$<>8__locals11.baseBonfireId);
							CS$<>8__locals11.baseAssetName = list11.Find((string s) => s.StartsWith("AEG"));
							MSBE.Part.Asset asset8 = null;
							if (flag7 && CS$<>8__locals11.baseAssetName == null)
							{
								asset8 = CS$<>8__locals9.CS$<>8__locals2.overworldAssetBase;
							}
							else if (CS$<>8__locals11.baseAssetName != null)
							{
								asset8 = CS$<>8__locals9.CS$<>8__locals2.msbs[d.ToMap].Parts.Assets.Find((MSBE.Part.Asset o) => o.Name == CS$<>8__locals11.baseAssetName);
							}
							else
							{
								if (CS$<>8__locals11.baseBonfireId <= 0)
								{
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(44, 3);
									defaultInterpolatedStringHandler.AppendLiteral("Internal error: no asset specified in ");
									defaultInterpolatedStringHandler.AppendFormatted(d.Base);
									defaultInterpolatedStringHandler.AppendLiteral(" in ");
									defaultInterpolatedStringHandler.AppendFormatted(d.Map);
									defaultInterpolatedStringHandler.AppendLiteral("->");
									defaultInterpolatedStringHandler.AppendFormatted(d.ToMap);
									throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
								}
								asset8 = CS$<>8__locals9.CS$<>8__locals2.msbs[d.ToMap].Parts.Assets.Find((MSBE.Part.Asset o) => (ulong)o.EntityID == (ulong)((long)(CS$<>8__locals11.baseBonfireId + 1000)));
							}
							if (asset8 == null)
							{
								throw new Exception("Can't move item to dungeon: " + d.Base + " missing from " + d.ToMap);
							}
							CS$<>8__locals11.baseEnemyName = list11.Find((string s) => s.StartsWith("c"));
							MSBE.Part.Enemy enemy = null;
							if (d.ShopEntity > 0 || CS$<>8__locals11.baseEnemyName != null)
							{
								if (CS$<>8__locals11.baseEnemyName != null)
								{
									enemy = CS$<>8__locals9.CS$<>8__locals2.msbs[d.ToMap].Parts.Enemies.Find((MSBE.Part.Enemy o) => o.Name == CS$<>8__locals11.baseEnemyName);
								}
								else
								{
									if (CS$<>8__locals11.baseBonfireId <= 0)
									{
										defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(42, 3);
										defaultInterpolatedStringHandler.AppendLiteral("Internal error: no NPC specified in ");
										defaultInterpolatedStringHandler.AppendFormatted(d.Base);
										defaultInterpolatedStringHandler.AppendLiteral(" in ");
										defaultInterpolatedStringHandler.AppendFormatted(d.Map);
										defaultInterpolatedStringHandler.AppendLiteral("->");
										defaultInterpolatedStringHandler.AppendFormatted(d.ToMap);
										throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
									}
									enemy = CS$<>8__locals9.CS$<>8__locals2.msbs[d.ToMap].Parts.Enemies.Find((MSBE.Part.Enemy o) => (ulong)o.EntityID == (ulong)((long)CS$<>8__locals11.baseBonfireId));
								}
							}
							List<MSBE.Part> list12 = new List<MSBE.Part>();
							if (d.ShopEntity > 0)
							{
								if (enemy == null)
								{
									throw new Exception("Can't move NPC to dungeon: " + d.Base + " missing from " + d.ToMap);
								}
								MSBE.Part.Enemy enemy2 = CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Parts.Enemies.Find((MSBE.Part.Enemy o) => (ulong)o.EntityID == (ulong)((long)d.ShopEntity));
								if (enemy2 == null)
								{
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(41, 2);
									defaultInterpolatedStringHandler.AppendLiteral("Can't move NPC to dungeon: ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(d.ShopEntity);
									defaultInterpolatedStringHandler.AppendLiteral(" missing from ");
									defaultInterpolatedStringHandler.AppendFormatted(d.Map);
									throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
								}
								if (!flag7)
								{
									string collisionPartName = enemy2.CollisionPartName;
								}
								list12.Add(enemy2);
							}
							else if (d.ObjectEntity > 0)
							{
								MSBE.Part.Asset asset9 = CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Parts.Assets.Find((MSBE.Part.Asset o) => (ulong)o.EntityID == (ulong)((long)d.ObjectEntity));
								if (asset9 == null)
								{
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 2);
									defaultInterpolatedStringHandler.AppendLiteral("Can't move asset to dungeon: ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(d.ObjectEntity);
									defaultInterpolatedStringHandler.AppendLiteral(" missing from ");
									defaultInterpolatedStringHandler.AppendFormatted(d.Map);
									throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
								}
								list12.Add(asset9);
							}
							else if (d.ItemLot != null)
							{
								string[] array = d.ItemLot.Split(' ', StringSplitOptions.None);
								for (i2 = 0; i2 < array.Length; i2++)
								{
									string s2 = array[i2];
									int lot = int.Parse(s2);
									MSBE.Event.Treasure treasure = CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Events.Treasures.Find((MSBE.Event.Treasure t) => t.ItemLotID == lot);
									string partName = (treasure != null) ? treasure.TreasurePartName : null;
									MSBE.Part.Asset source = CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Parts.Assets.Find((MSBE.Part.Asset o) => o.Name == partName);
									if (treasure == null || partName == null || source == null)
									{
										defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 3);
										defaultInterpolatedStringHandler.AppendLiteral("Can't move item to dungeon: lot ");
										defaultInterpolatedStringHandler.AppendFormatted<int>(lot);
										defaultInterpolatedStringHandler.AppendLiteral(" (name ");
										defaultInterpolatedStringHandler.AppendFormatted(partName);
										defaultInterpolatedStringHandler.AppendLiteral(") missing from ");
										defaultInterpolatedStringHandler.AppendFormatted(d.Map);
										throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
									}
									if (!list12.Any((MSBE.Part s) => s.Name == source.Name))
									{
										list12.Add(source);
									}
								}
							}
							if (list12.Count == 0)
							{
								throw new Exception("Internal error: no source defined for item in " + d.Map);
							}
							List<MSBE.Part> source2 = list12.ToList<MSBE.Part>();
							CS$<>8__locals11.mainSource = list12[0];
							if (!d.HasTag("onlyitem"))
							{
								GameDataWriterE.<>c__DisplayClass1_15 CS$<>8__locals13 = new GameDataWriterE.<>c__DisplayClass1_15();
								CS$<>8__locals13.CS$<>8__locals3 = CS$<>8__locals11;
								CS$<>8__locals13.assocModels = dictionary11[text17];
								foreach (KeyValuePair<string, HashSet<string>> keyValuePair in dictionary13)
								{
									string text18;
									HashSet<string> hashSet3;
									keyValuePair.Deconstruct(out text18, out hashSet3);
									string tag = text18;
									HashSet<string> second = hashSet3;
									if (d.HasTag(tag))
									{
										CS$<>8__locals13.assocModels = new HashSet<string>(CS$<>8__locals13.assocModels.Except(second));
									}
								}
								CS$<>8__locals13.extraHelpers = new HashSet<uint>();
								if (d.HelperObjects != null)
								{
									CS$<>8__locals13.extraHelpers.UnionWith(d.HelperObjects.Split(' ', StringSplitOptions.None).Select(new Func<string, uint>(uint.Parse)));
								}
								CS$<>8__locals13.dist = 100;
								if (d.HasTag("iji"))
								{
									CS$<>8__locals13.dist = 1000;
								}
								else if (d.HasTag("hangingbell"))
								{
									CS$<>8__locals13.dist = 2000;
								}
								list12.AddRange(CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Parts.Assets.Where(new Func<MSBE.Part.Asset, bool>(CS$<>8__locals13.<Write>g__isCloseExtraSource|128)));
								if (CS$<>8__locals13.assocModels.Any((string m) => m.StartsWith("c")))
								{
									goto IL_39DE;
								}
								if (CS$<>8__locals13.extraHelpers.Any((uint id) => id % 10000U < 1000U))
								{
									goto IL_39DE;
								}
								IL_3A1E:
								List<string> list13 = new List<string>();
								if (d.ExtraMap != null)
								{
									list13.Add(d.ExtraMap);
								}
								if (d.HasTag("raceshop"))
								{
									byte[] array12 = GameDataWriterE.<Write>g__parseMap|1_32(d.Map);
									if ((array12[0] != 60 && array12[0] != 61) || array12[3] != 0)
									{
										throw new Exception(d.Map);
									}
									list13.Add(GameDataWriterE.<Write>g__parentMap|1_34(array12, !d.HasTag("mid")));
								}
								else if (d.HasTag("cross"))
								{
									byte[] mapBytes = GameDataWriterE.<Write>g__parseMap|1_32(d.Map);
									list13.Add(GameDataWriterE.<Write>g__parentMap|1_34(mapBytes, true));
								}
								foreach (string text19 in list13)
								{
									if (CS$<>8__locals9.CS$<>8__locals2.opt["debugmove"])
									{
										Console.WriteLine(d.Map + " -> " + text19);
									}
									Vector3 right2 = CS$<>8__locals9.CS$<>8__locals2.<Write>g__getMapOffset|28(text19, d.Map);
									foreach (MSBE.Part part in CS$<>8__locals9.CS$<>8__locals2.msbs[text19].Parts.Assets)
									{
										if (CS$<>8__locals13.<Write>g__isExtraSource|127(part))
										{
											Vector3 vector10 = part.Position + right2;
											if (Vector3.DistanceSquared(vector10, CS$<>8__locals13.CS$<>8__locals3.mainSource.Position) < (float)CS$<>8__locals13.dist)
											{
												MSBE.Part part2 = part.DeepCopy();
												part2.Position = vector10;
												list12.Add(part2);
												if (CS$<>8__locals9.CS$<>8__locals2.opt["debugmove"])
												{
													defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 4);
													defaultInterpolatedStringHandler.AppendLiteral("Part ");
													defaultInterpolatedStringHandler.AppendFormatted(part.Name);
													defaultInterpolatedStringHandler.AppendLiteral(" from ");
													defaultInterpolatedStringHandler.AppendFormatted(d.Map);
													defaultInterpolatedStringHandler.AppendLiteral(" -> ");
													defaultInterpolatedStringHandler.AppendFormatted(text19);
													defaultInterpolatedStringHandler.AppendLiteral(", dist ");
													defaultInterpolatedStringHandler.AppendFormatted<float>(Vector3.Distance(vector10, CS$<>8__locals13.CS$<>8__locals3.mainSource.Position));
													Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
												}
											}
										}
									}
								}
								goto IL_3CC7;
								IL_39DE:
								list12.AddRange(CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Parts.Enemies.Where(new Func<MSBE.Part.Enemy, bool>(CS$<>8__locals13.<Write>g__isCloseExtraSource|128)));
								goto IL_3A1E;
							}
							IL_3CC7:
							Vector3 vector11 = CS$<>8__locals11.mainSource.Position;
							Vector3 rotation3 = CS$<>8__locals11.mainSource.Rotation;
							if (d.HasTag("revered"))
							{
								MSBE.Part part3 = list12.MinBy((MSBE.Part e) => e.Position.Y);
								if (d.HasTag("alignitem"))
								{
									vector11 = new Vector3(vector11.X, part3.Position.Y, vector11.Z);
								}
								else
								{
									vector11 = part3.Position;
									rotation3 = part3.Rotation;
								}
							}
							else if (d.HasTag("fragment"))
							{
								List<MSBE.Part> list14 = list12;
								Predicate<MSBE.Part> match2;
								if ((match2 = CS$<>8__locals9.<>9__132) == null)
								{
									match2 = (CS$<>8__locals9.<>9__132 = ((MSBE.Part e) => CS$<>8__locals9.fragmentStatues.Contains(e.ModelName)));
								}
								MSBE.Part part4 = list14.Find(match2);
								if (part4 != null)
								{
									vector11 = part4.Position;
									rotation3 = part4.Rotation;
								}
							}
							else if (d.HasTag("hangingbell"))
							{
								vector11 -= right;
							}
							List<float> list15 = GameDataWriterE.<Write>g__parseFloats|1_20(d.Location.Split(' ', StringSplitOptions.None));
							Vector3 vector12 = new Vector3(list15[0], list15[1], list15[2]);
							bool flag8 = list15.Count == 6;
							Vector3 vector13 = flag8 ? new Vector3(list15[4], list15[3], list15[5]) : new Vector3(0f, list15[3], 0f);
							Matrix4x4 matrix = Matrix4x4.CreateFromYawPitchRoll(-rotation3.Y * 0.017453292f, 0f, 0f);
							Matrix4x4 matrix2 = Matrix4x4.CreateFromYawPitchRoll(vector13.Y * 0.017453292f, vector13.X * 0.017453292f, vector13.Z * 0.017453292f);
							List<MSBE.Part> list16 = new List<MSBE.Part>();
							using (List<MSBE.Part>.Enumerator enumerator11 = list12.GetEnumerator())
							{
								while (enumerator11.MoveNext())
								{
									MSBE.Part source = enumerator11.Current;
									Vector3 vector14 = Vector3.Add(Vector3.Transform(Vector3.Transform(Vector3.Subtract(source.Position, vector11), matrix), matrix2), vector12);
									Vector3 vector15 = new Vector3(0f, source.Rotation.Y - rotation3.Y + vector13.Y, 0f);
									if (flag8)
									{
										vector15 = new Vector3(vector13.X, source.Rotation.Y - rotation3.Y + vector13.Y, vector13.Z);
									}
									if (d.HasTag("align"))
									{
										vector14 = new Vector3(vector14.X, Math.Min(vector14.Y, vector12.Y), vector14.Z);
									}
									else if (d.HasTag("alignfull"))
									{
										vector14 = new Vector3(vector14.X, vector12.Y, vector14.Z);
									}
									if (CS$<>8__locals9.CS$<>8__locals2.opt["debugmove"])
									{
										defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(10, 1);
										defaultInterpolatedStringHandler.AppendLiteral("- copying ");
										defaultInterpolatedStringHandler.AppendFormatted<MSBE.Part>(source);
										Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
									}
									MSBE.Part.Asset asset10 = source as MSBE.Part.Asset;
									if (asset10 != null)
									{
										MSBE.Event.Treasure treasure2 = null;
										MSBE.Event.ObjAct objAct2 = null;
										List<string> list17 = new List<string>
										{
											d.ToMap
										};
										if (d.HasTag("alttarget"))
										{
											list17.Add(GameDataWriterE.<Write>g__getAltMap|1_35(d.ToMap));
										}
										Predicate<MSBE.Event.Treasure> <>9__133;
										Predicate<MSBE.Event.ObjAct> <>9__134;
										foreach (string text20 in list17)
										{
											MSBE.Part.Asset asset11;
											if (flag7)
											{
												asset11 = (MSBE.Part.Asset)asset8.DeepCopy();
												asset11.ModelName = source.ModelName;
												GameDataWriterE.<Write>g__addAssetModel|1_21(CS$<>8__locals9.CS$<>8__locals2.msbs[text20], asset11.ModelName);
												GameDataWriterE.<Write>g__setAssetName|1_24(asset8, CS$<>8__locals9.CS$<>8__locals2.<Write>g__newPartName|12(text20, asset11.ModelName, asset8.Name));
												asset11.Position = vector14;
												asset11.Rotation = vector15;
												CS$<>8__locals9.CS$<>8__locals2.msbs[text20].Parts.Assets.Add(asset11);
											}
											else
											{
												asset11 = CS$<>8__locals9.CS$<>8__locals2.<Write>g__addFakeGate|25(text20, source.ModelName, asset8.Name, vector14, vector15, null);
											}
											if (source.EntityID > 0U)
											{
												asset11.EntityID = num4++;
												Util.AddMulti<int, ValueTuple<string, int>>(dictionary9, (int)source.EntityID, new ValueTuple<string, int>(text20, (int)asset11.EntityID));
											}
											else
											{
												asset11.EntityID = 0U;
											}
											Array.Clear(asset11.EntityGroupIDs);
											asset11.AssetSfxParamRelativeID = asset10.AssetSfxParamRelativeID;
											asset11.UnkT58 = asset10.UnkT58;
											if (treasure2 == null)
											{
												List<MSBE.Event.Treasure> treasures = CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Events.Treasures;
												Predicate<MSBE.Event.Treasure> match3;
												if ((match3 = <>9__133) == null)
												{
													match3 = (<>9__133 = ((MSBE.Event.Treasure t) => t.TreasurePartName == source.Name));
												}
												treasure2 = treasures.Find(match3);
											}
											if (treasure2 != null)
											{
												MSBE.Event.Treasure treasure3 = (MSBE.Event.Treasure)treasure2.DeepCopy();
												MSBE.Event.Treasure treasure4 = treasure3;
												treasure4.Name += " copy";
												treasure3.TreasurePartName = asset11.Name;
												CS$<>8__locals9.CS$<>8__locals2.msbs[text20].Events.Treasures.Add(treasure3);
											}
											if (d.HasTag("hangingbell"))
											{
												if (objAct2 == null)
												{
													List<MSBE.Event.ObjAct> objActs = CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Events.ObjActs;
													Predicate<MSBE.Event.ObjAct> match4;
													if ((match4 = <>9__134) == null)
													{
														match4 = (<>9__134 = ((MSBE.Event.ObjAct t) => t.ObjActPartName == source.Name));
													}
													objAct2 = objActs.Find(match4);
												}
												if (objAct2 != null)
												{
													MSBE.Event.ObjAct objAct3 = (MSBE.Event.ObjAct)objAct2.DeepCopy();
													MSBE.Event.ObjAct objAct4 = objAct3;
													objAct4.Name += " copy";
													objAct3.ObjActPartName = asset11.Name;
													CS$<>8__locals9.CS$<>8__locals2.msbs[text20].Events.ObjActs.Add(objAct3);
												}
											}
											list16.Add(asset11);
										}
										if (treasure2 != null)
										{
											CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Events.Treasures.Remove(treasure2);
										}
										if (objAct2 != null)
										{
											CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Events.ObjActs.Remove(objAct2);
										}
									}
									else
									{
										MSBE.Part.Enemy enemy3 = source as MSBE.Part.Enemy;
										if (enemy3 == null)
										{
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 1);
											defaultInterpolatedStringHandler.AppendLiteral("Internal error: Unknown item moving source ");
											defaultInterpolatedStringHandler.AppendFormatted<MSBE.Part>(source);
											throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
										}
										if (d.HasTag("alttarget"))
										{
											throw new NotImplementedException("alttarget not supported for " + d.Map + "->" + d.ToMap);
										}
										MSBE.Part.Enemy enemy4 = (MSBE.Part.Enemy)enemy.DeepCopy();
										enemy4.EntityID = num4++;
										if (source.EntityID > 0U && d.ShopEntity > 0)
										{
											Util.AddMulti<int, ValueTuple<string, int>>(dictionary9, (int)source.EntityID, new ValueTuple<string, int>(d.ToMap, (int)enemy4.EntityID));
											GameDataWriterE.<>c__DisplayClass1_0 CS$<>8__locals15 = CS$<>8__locals9.CS$<>8__locals2;
											string toMap = d.ToMap;
											Events events2 = CS$<>8__locals9.CS$<>8__locals2.events;
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(33, 1);
											defaultInterpolatedStringHandler.AppendLiteral("SetCharacterBackreadState(");
											defaultInterpolatedStringHandler.AppendFormatted<uint>(enemy4.EntityID);
											defaultInterpolatedStringHandler.AppendLiteral(", true)");
											CS$<>8__locals15.<Write>g__addInit|46(toMap, events2.ParseAdd(defaultInterpolatedStringHandler.ToStringAndClear()), 50);
										}
										Array.Clear(enemy4.EntityGroupIDs);
										enemy4.ModelName = enemy3.ModelName;
										enemy4.ThinkParamID = enemy3.ThinkParamID;
										enemy4.NPCParamID = enemy3.NPCParamID;
										enemy4.CharaInitID = enemy3.CharaInitID;
										if (enemy3.TalkID > 0)
										{
											enemy4.TalkID = CS$<>8__locals9.CS$<>8__locals2.<Write>g__copyEsd|10(enemy3.TalkID, d.ToMap);
										}
										else
										{
											enemy4.TalkID = 0;
										}
										enemy4.WalkRouteName = null;
										if (d.HasTag("academykey"))
										{
											enemy4.ModelName = "c3702";
											enemy4.ThinkParamID = 0;
											enemy4.NPCParamID = 37020020;
											enemy4.CharaInitID = -1;
											CS$<>8__locals9.CS$<>8__locals2.<Write>g__addInit|46(d.ToMap, new EMEVD.Instruction(2000, 6, new object[]
											{
												0,
												90005201,
												enemy4.EntityID,
												30010,
												-1,
												0,
												0,
												0,
												0,
												0,
												0
											}), 0);
										}
										GameDataWriterE.<Write>g__addEnemyModel|1_22(CS$<>8__locals9.CS$<>8__locals2.msbs[d.ToMap], enemy4.ModelName);
										enemy4.Name = CS$<>8__locals9.CS$<>8__locals2.<Write>g__newPartName|12(d.ToMap, enemy4.ModelName, enemy.Name);
										GameDataWriterE.<Write>g__setNameIdent|1_23(enemy4);
										enemy4.Position = vector14;
										enemy4.Rotation = vector15;
										CS$<>8__locals9.CS$<>8__locals2.msbs[d.ToMap].Parts.Enemies.Add(enemy4);
										list16.Add(enemy4);
									}
								}
							}
							if (d.RemoveDest != null)
							{
								HashSet<string> parts = new HashSet<string>(EventConfig.PhraseRe.Split(d.RemoveDest));
								CS$<>8__locals9.CS$<>8__locals2.msbs[d.ToMap].Parts.Assets.RemoveAll((MSBE.Part.Asset e) => parts.Contains(e.Name));
							}
							if (!d.HasTag("noremove"))
							{
								HashSet<string> removeNames = new HashSet<string>(from e in source2
								select e.Name);
								CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Parts.Enemies.RemoveAll((MSBE.Part.Enemy e) => removeNames.Contains(e.Name));
								CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Parts.Assets.RemoveAll((MSBE.Part.Asset e) => removeNames.Contains(e.Name));
								MSBE.Region.Message message2 = (MSBE.Region.Message)message.DeepCopy();
								MSBE.Entry entry4 = message2;
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(19, 3);
								defaultInterpolatedStringHandler.AppendLiteral("Relocation for ");
								defaultInterpolatedStringHandler.AppendFormatted(d.ItemLot);
								defaultInterpolatedStringHandler.AppendFormatted<int>(d.ShopEntity);
								defaultInterpolatedStringHandler.AppendLiteral(" to ");
								defaultInterpolatedStringHandler.AppendFormatted(d.Text);
								entry4.Name = defaultInterpolatedStringHandler.ToStringAndClear();
								message2.Position = CS$<>8__locals11.mainSource.Position;
								message2.Rotation = CS$<>8__locals11.mainSource.Rotation;
								if (d.HasTag("interrain"))
								{
									message2.Position = GameDataWriterE.<Write>g__moveInDirection|1_29(message2.Position, message2.Rotation, -1.25f);
								}
								if (d.HasTag("hangingbell"))
								{
									message2.Position -= right;
								}
								message2.MessageID = CS$<>8__locals9.<Write>g__newBloodMsg|110("Relocated to " + d.Text.Replace("|", "\n"));
								CS$<>8__locals9.CS$<>8__locals2.msbs[d.Map].Regions.Add(message2);
								if (d.HasTag("altmap"))
								{
									string text21 = GameDataWriterE.<Write>g__getAltMap|1_35(d.Map);
									if (d.ShopEntity == 1045360700)
									{
										CS$<>8__locals9.CS$<>8__locals2.msbs[text21].Parts.Enemies.RemoveAll((MSBE.Part.Enemy e) => (ulong)e.EntityID == (ulong)((long)(d.ShopEntity + 2)));
									}
									else if (d.ItemLot == "2044450000")
									{
										string altName = "AEG463_625_9002";
										CS$<>8__locals9.CS$<>8__locals2.msbs[text21].Parts.Assets.RemoveAll((MSBE.Part.Asset e) => e.Name == altName);
										CS$<>8__locals9.CS$<>8__locals2.msbs[text21].Events.Treasures.RemoveAll((MSBE.Event.Treasure e) => e.TreasurePartName == altName);
									}
									CS$<>8__locals9.CS$<>8__locals2.msbs[text21].Regions.Add(message2);
									CS$<>8__locals9.CS$<>8__locals2.writeMsbs.Add(text21);
								}
								CS$<>8__locals9.CS$<>8__locals2.writeMsbs.Add(d.Map);
							}
							CS$<>8__locals9.CS$<>8__locals2.writeMsbs.Add(d.ToMap);
						}
					}
				}
				MSBE.Part.Asset asset12 = CS$<>8__locals9.CS$<>8__locals2.msbs["m14_00_00_00"].Parts.Assets.Find((MSBE.Part.Asset a) => a.Name == "AEG258_104_3014");
				if (asset12 != null)
				{
					CS$<>8__locals9.CS$<>8__locals2.<Write>g__addFakeGate|25("m14_00_00_00", "AEG250_007", asset12.Name, new Vector3(126.691f, 90.289f, -175.041f), new Vector3(0f, 9.312f, 0f), null);
				}
				MSBE.Part.Asset asset13 = CS$<>8__locals9.CS$<>8__locals2.msbs["m60_43_31_00"].Parts.Assets.Find((MSBE.Part.Asset a) => a.Name == "AEG001_290_7245");
				if (asset13 != null)
				{
					CS$<>8__locals9.CS$<>8__locals2.<Write>g__addFakeGate|25("m60_43_31_00", "AEG030_925", asset13.Name, new Vector3(54.047f, 95.033f, -80.946f), new Vector3(0f, 119.743f, 0f), null).AssetSfxParamRelativeID = 0;
				}
				MSBE.Region.Message message3 = (MSBE.Region.Message)message.DeepCopy();
				message3.Name = "Kale NPC relocation";
				message3.Position = new Vector3(-283.068f, -22.593f, -310.354f);
				message3.Rotation = new Vector3(0f, 96.868f, 0f);
				message3.MessageID = CS$<>8__locals9.<Write>g__newBloodMsg|110("Open for business downstairs");
				message3.CharacterModelName = 3200;
				message3.NPCParamID = 32000010;
				message3.AnimationID = 30014;
				string key = "m11_10_00_00";
				CS$<>8__locals9.CS$<>8__locals2.msbs[key].Regions.Add(message3);
				CS$<>8__locals9.shopIds = GameEditor.ParamToDictionary(CS$<>8__locals9.CS$<>8__locals2.Params["ShopLineupParam"]);
				if (CS$<>8__locals9.CS$<>8__locals2.opt["lantern"])
				{
					int num25 = CS$<>8__locals9.<Write>g__findNewShopId|113(101800, 101899);
					if (num25 > 0)
					{
						CS$<>8__locals9.<Write>g__createNewShop|114(num25, 2070, 1800, 1, 160450U);
					}
				}
				if (CS$<>8__locals9.CS$<>8__locals2.opt["stoneshop"])
				{
					SortedDictionary<string, int> sortedDictionary = new SortedDictionary<string, int>();
					foreach (AnnotationData.DungeonItem dungeonItem in ann.DungeonItems)
					{
						int value4;
						if (dungeonItem.ShopRange != null && !dungeonItem.HasTag("smallshop") && CS$<>8__locals9.CS$<>8__locals2.g.AreaTiers.TryGetValue(dungeonItem.ToArea, out value4))
						{
							sortedDictionary[dungeonItem.ShopRange] = value4;
						}
					}
					List<string> list18 = (from e in sortedDictionary
					orderby new ValueTuple<int, uint>(e.Value, base.<Write>g__hashArea|143(e.Key))
					select e.Key).ToList<string>();
					List<string> order = list18;
					int item2 = 3;
					int num26 = 3;
					PARAM.Row row3 = CS$<>8__locals9.CS$<>8__locals2.Params["EquipMtrlSetParam"][1];
					if (row3 != null && (sbyte)row3["itemNum01"].Value > 1)
					{
						item2 = 8;
						num26 = 1;
					}
					CS$<>8__locals9.<Write>g__addAmounts|146(list18, new List<int>
					{
						item2,
						item2,
						0,
						0,
						item2,
						0,
						0
					}, 10100, 200 * num26);
					CS$<>8__locals9.<Write>g__addAmounts|146(list18, new List<int>
					{
						0,
						0,
						item2,
						0,
						item2,
						0,
						0
					}, 10101, 400 * num26);
					CS$<>8__locals9.<Write>g__addAmounts|146(list18, new List<int>
					{
						0,
						0,
						0,
						0,
						0,
						item2,
						item2
					}, 10102, 600 * num26);
					CS$<>8__locals9.<Write>g__addAmounts|146(list18, new List<int>
					{
						0,
						0,
						0,
						0,
						0,
						0,
						item2
					}, 10103, 900 * num26);
					CS$<>8__locals9.<Write>g__addAmounts|146(order, new List<int>
					{
						1,
						0,
						2,
						2,
						0,
						0,
						0
					}, 10160, 3000);
					CS$<>8__locals9.<Write>g__addAmounts|146(order, new List<int>
					{
						0,
						1,
						0,
						2,
						0,
						0,
						0
					}, 10161, 4000);
					CS$<>8__locals9.<Write>g__addAmounts|146(order, new List<int>
					{
						0,
						0,
						0,
						0,
						1,
						0,
						1
					}, 10162, 5000);
					CS$<>8__locals9.<Write>g__addAmounts|146(order, new List<int>
					{
						0,
						0,
						0,
						0,
						0,
						1,
						0
					}, 10163, 7000);
					CS$<>8__locals9.CS$<>8__locals2.Params["ShopLineupParam"].Rows = (from r in CS$<>8__locals9.CS$<>8__locals2.Params["ShopLineupParam"].Rows
					orderby r.ID
					select r).ToList<PARAM.Row>();
				}
			}
			HashSet<int> randomFogs = new HashSet<int>(from e in ann.Entrances.Concat(ann.Warps)
			where (!e.HasTag("unused") || e.HasTag("alwaysshow")) && !e.IsFixed
			select e.ID);
			CS$<>8__locals1.defeatFlagAreas = (from a in CS$<>8__locals1.g.Areas.Values
			where a.DefeatFlag > 0
			select a).ToDictionary((AnnotationData.Area a) => a.DefeatFlag, (AnnotationData.Area a) => a);
			CS$<>8__locals1.defeatFlagAreas[76422] = CS$<>8__locals1.g.Areas["caelid_radahn"];
			CS$<>8__locals1.defeatFlagAreas[34130800] = CS$<>8__locals1.g.Areas["caelid_tower_postboss"];
			CS$<>8__locals1.defeatFlagAreas[31000845] = CS$<>8__locals1.g.Areas["limgrave_murkwatercave_boss"];
			CS$<>8__locals1.defeatFlagAreas[1048570350] = CS$<>8__locals1.g.Areas["snowfield_evergaol"];
			CS$<>8__locals1.defeatFlagAreas[42007000] = CS$<>8__locals1.g.Areas["gravesite_forge"];
			CS$<>8__locals1.defeatFlagAreas[42027000] = CS$<>8__locals1.g.Areas["scadualtus_forge"];
			CS$<>8__locals1.defeatFlagAreas[42037000] = CS$<>8__locals1.g.Areas["rauhbase_forge"];
			int num27 = CS$<>8__locals1.g.UnlockTiers.FindLastIndex((int tier) => tier >= 0);
			uint num28 = (num27 >= 0) ? (num6 + (uint)num27) : 0U;
			CS$<>8__locals1.edits = new EventEditor(CS$<>8__locals1.opt, CS$<>8__locals1.events, eventConfig, CS$<>8__locals1.writeEmevds);
			data = new EventEditor.GameData
			{
				RandomFogs = randomFogs,
				WarpDests = dictionary7,
				ReturnWarps = dictionary8,
				AlternateWarps = hashSet2,
				MoveNpcs = dictionary9,
				MaxTierFlag = num28
			};
			EventEditor.GameFuncs gameFuncs = new EventEditor.GameFuncs
			{
				WarpCmds = new Func<AnnotationData.Side, List<string>>(CS$<>8__locals1.<Write>g__warpToSide|38),
				DefeatFlagEntrance = new Func<int, AnnotationData.Side>(CS$<>8__locals1.<Write>g__defeatFlagEntrance|44)
			};
			CS$<>8__locals1.edits.Process(CS$<>8__locals1.emevds, data, gameFuncs);
			AnnotationData.Entrance ashenGate = ann.Entrances.Find((AnnotationData.Entrance e) => e.SplitFrom == "m60_45_52_00_AEG099_002_9000");
			CS$<>8__locals1.edits.CopyLeyndellGate(CS$<>8__locals1.emevds, ashenGate);
			foreach (KeyValuePair<string, EMEVD> keyValuePair2 in CS$<>8__locals1.emevds)
			{
				if (!keyValuePair2.Key.StartsWith("common"))
				{
					foreach (EMEVD.Event @event in keyValuePair2.Value.Events)
					{
						bool flag9 = false;
						bool flag10 = false;
						foreach (EMEVD.Instruction instruction in @event.Instructions)
						{
							flag9 |= (instruction.Bank == 2003 && instruction.ID == 12);
							flag10 |= (instruction.Bank == 2000 && instruction.ID == 5);
						}
						if (flag9 && !flag10)
						{
							@event.Instructions.Add(new EMEVD.Instruction(2000, 5, new List<object>
							{
								0
							}));
						}
					}
				}
			}
			CS$<>8__locals1.customEvents = new Dictionary<string, EventConfig.NewEvent>();
			foreach (EventConfig.NewEvent newEvent in eventConfig.NewEvents)
			{
				EMEVD.Event event2 = null;
				if (newEvent.Commands != null)
				{
					bool flag11 = (newEvent.Name != null && newEvent.Name.Contains("restart")) || newEvent.HasTag("restart");
					event2 = new EMEVD.Event((long)newEvent.ID, flag11 ? 1 : 0);
					List<string> list19 = CS$<>8__locals1.events.Decomment(newEvent.Commands);
					if (CS$<>8__locals1.opt["tips"])
					{
						list19.RemoveAll((string c) => c == "ShowTextOnLoadingScreen(Disabled)");
					}
					for (int m2 = 0; m2 < list19.Count; m2++)
					{
						ValueTuple<EMEVD.Instruction, List<EMEVD.Parameter>> valueTuple2 = CS$<>8__locals1.events.ParseAddArg(list19[m2], m2);
						EMEVD.Instruction item3 = valueTuple2.Item1;
						List<EMEVD.Parameter> item4 = valueTuple2.Item2;
						event2.Instructions.Add(item3);
						event2.Parameters.AddRange(item4);
					}
				}
				if (newEvent.Name == null)
				{
					CS$<>8__locals1.emevds["common"].Events.Add(event2);
					CS$<>8__locals1.emevds["common"].Events[0].Instructions.Add(new EMEVD.Instruction(2000, 0, new List<object>
					{
						0,
						(int)event2.ID,
						0
					}));
				}
				else if (newEvent.Name.StartsWith("common"))
				{
					if (event2 != null)
					{
						CS$<>8__locals1.emevds["common"].Events.Add(event2);
					}
					CS$<>8__locals1.customEvents[newEvent.Name] = newEvent;
				}
				else
				{
					if (event2 != null)
					{
						CS$<>8__locals1.emevds["common_func"].Events.Add(event2);
					}
					CS$<>8__locals1.customEvents[newEvent.Name] = newEvent;
				}
			}
			List<uint> list20 = new List<uint>();
			foreach (PARAM.Row row4 in CS$<>8__locals1.Params["PlayRegionParam"].Rows)
			{
				uint num29 = (uint)row4["pcPositionSaveLimitEventFlagId"].Value;
				if (num29 > 0U)
				{
					int num30 = list20.IndexOf(num29);
					bool flag12 = false;
					if (num30 == -1)
					{
						list20.Add(num29);
						num30 = list20.Count - 1;
						flag12 = true;
					}
					uint num31 = (uint)((ulong)num10 + (ulong)((long)num30));
					row4["pcPositionSaveLimitEventFlagId"].Value = num31;
					if (flag12)
					{
						CS$<>8__locals1.<Write>g__addCommonInit|48("common_makestable", num30, new List<object>
						{
							num31,
							num29
						});
					}
				}
			}
			num10 += (uint)list20.Count;
			if (CS$<>8__locals1.opt["roundtable"])
			{
				CS$<>8__locals1.<Write>g__addCommonInit|48(CS$<>8__locals1.opt[Feature.ChapelInit] ? "common_gracetable" : "common_roundtable", 0, new List<object>
				{
					0
				});
			}
			else if (CS$<>8__locals1.opt[Feature.Segmented])
			{
				CS$<>8__locals1.<Write>g__addCommonInit|48("common_faketable", 0, new List<object>
				{
					0
				});
			}
			CS$<>8__locals1.<Write>g__addCommonFuncInit|47("festivalblaidd", "m60_52_38_00", new List<object>
			{
				0
			}, 0);
			if (CS$<>8__locals1.emevds["common"].Events.Any((EMEVD.Event e) => e.ID == 901718550L))
			{
				int[] array2 = new int[]
				{
					901718550,
					901718559
				};
				for (i2 = 0; i2 < array2.Length; i2++)
				{
					int id = array2[i2];
					EMEVD.Event event3 = CS$<>8__locals1.emevds["common"].Events.Find((EMEVD.Event e) => e.ID == (long)id);
					if (event3 != null)
					{
						event3.Instructions.RemoveAll((EMEVD.Instruction ins) => ins.Bank == 2003 && (ins.ID == 23 || ins.ID == 14));
					}
				}
				CS$<>8__locals1.<Write>g__addCommonInit|48("common_dlcstart", 0, new List<object>
				{
					0
				});
				CS$<>8__locals1.<Write>g__addCommonInit|48("common_dlcdoor", 0, new List<object>
				{
					0
				});
				MSBE.Part.Asset asset14 = CS$<>8__locals1.msbs["m10_01_00_00"].Parts.Assets.Find((MSBE.Part.Asset a) => a.Name == "AEG219_002_0500");
				if (asset14 == null)
				{
					throw new Exception("Trying to merge DLC Start but missing chapel door AEG219_002_0500 in m10_01_00_00");
				}
				CS$<>8__locals1.msbs["m10_01_00_00"].Events.ObjActs.RemoveAll((MSBE.Event.ObjAct oa) => oa.ObjActPartName == "AEG219_002_0500");
				asset14.EntityID = 901718560U;
			}
			else
			{
				CS$<>8__locals1.<Write>g__addCommonInit|48("common_fingerstart", 0, new List<object>
				{
					0
				});
				CS$<>8__locals1.<Write>g__addCommonInit|48("common_fingerdoor", 0, new List<object>
				{
					0
				});
			}
			HashSet<string> hashSet4 = new HashSet<string>
			{
				"c0100",
				"c1000",
				"c0110"
			};
			bool flag13 = CS$<>8__locals1.g.ExcludeMode == AnnotationData.AreaMode.None;
			if (flag13)
			{
				PARAM.Row row5 = CS$<>8__locals1.Params["SpEffectParam"][20000100];
				PARAM.Row row6 = CS$<>8__locals1.Params["SpEffectParam"][20000200];
				foreach (PARAM.Row row7 in CS$<>8__locals1.Params["SpEffectParam"].Rows)
				{
					if (row7.ID > 20000100 && row7.ID <= 20000120)
					{
						GameEditor.CopyRow(row5, row7);
					}
					if (row7.ID > 20000200 && row7.ID <= 20000210)
					{
						GameEditor.CopyRow(row6, row7);
					}
				}
			}
			EldenScaling eldenScaling = new EldenScaling(CS$<>8__locals1.Params);
			Dictionary<int, int> dictionary14 = eldenScaling.InitializeEldenScaling();
			EldenScaling.SpEffectValues spEffectValues = eldenScaling.EditScalingSpEffects();
			uint num32 = 1028660000U;
			if ((CS$<>8__locals1.opt["scale"] || CS$<>8__locals1.opt.GetInt("minscale", out i2) || CS$<>8__locals1.opt.GetInt("maxscale", out i2)) && CS$<>8__locals1.g.AreaTiers != null)
			{
				Dictionary<int, EMEVD.Instruction> dictionary15 = new Dictionary<int, EMEVD.Instruction>();
				Dictionary<int, List<EMEVD.Instruction>> dictionary16 = new Dictionary<int, List<EMEVD.Instruction>>();
				HashSet<int> hashSet5 = new HashSet<int>(spEffectValues.Areas.Values.SelectMany((EldenScaling.AreaScalingValue a) => new int[]
				{
					a.RegularScaling,
					a.FixedScaling,
					a.UniqueRegularScaling,
					a.UniqueFixedScaling
				}));
				foreach (KeyValuePair<string, EMEVD> keyValuePair3 in CS$<>8__locals1.emevds)
				{
					if (CS$<>8__locals1.mergedMods.Count == 0)
					{
						break;
					}
					foreach (EMEVD.Event event4 in keyValuePair3.Value.Events)
					{
						foreach (EMEVD.Instruction instruction2 in event4.Instructions)
						{
							if (instruction2.Bank == 2000 && instruction2.ID == 6 && instruction2.ArgData.Length == 16)
							{
								List<object> list21 = instruction2.UnpackArgs(Enumerable.Repeat<EMEVD.Instruction.ArgType>(5, instruction2.ArgData.Length / 4), false);
								int num33 = (int)list21[1];
								if (num33 == 9005890 || num33 == 9005891)
								{
									int key2 = (int)list21[2];
									int item5 = (int)list21[3];
									if (hashSet5.Contains(item5))
									{
										if (dictionary15.ContainsKey(key2))
										{
											Util.AddMulti<int, EMEVD.Instruction>(dictionary16, key2, instruction2);
										}
										else
										{
											dictionary15[key2] = instruction2;
										}
										CS$<>8__locals1.writeEmevds.Add(keyValuePair3.Key);
									}
								}
							}
						}
					}
				}
				int num34 = 0;
				int num35 = 0;
				int num36 = 0;
				int num37 = 0;
				int num38 = 0;
				Dictionary<ValueTuple<string, string>, AnnotationData.EnemyLoc> dictionary17 = ann.Locations.Enemies.ToDictionary((AnnotationData.EnemyLoc e) => new ValueTuple<string, string>(e.Map, e.ID), (AnnotationData.EnemyLoc e) => e);
				Dictionary<int, int> dictionary18 = CS$<>8__locals1.Params["NpcParam"].Rows.ToDictionary((PARAM.Row r) => r.ID, (PARAM.Row r) => (int)r["spEffectID3"].Value);
				HashSet<string> hashSet6 = new HashSet<string>
				{
					"c4710"
				};
				HashSet<int> hashSet7 = new HashSet<int>
				{
					526100965,
					526100052
				};
				HashSet<int> hashSet8 = new HashSet<int>
				{
					23611,
					23612,
					23701,
					23711
				};
				HashSet<string> hashSet9 = new HashSet<string>();
				hashSet9.Add("c3181");
				hashSet9.Add("c4640");
				hashSet9.Add("c3400");
				hashSet9.Add("c7100");
				hashSet9.Add("c4290");
				hashSet9.Add("c3350");
				hashSet9.Add("c4130");
				hashSet9.Add("c4810");
				hashSet9.Add("c4811");
				hashSet9.Add("c4502");
				hashSet9.Add("c4500");
				hashSet9.Add("c4980");
				hashSet9.Add("c3150");
				hashSet9.Add("c3160");
				hashSet9.Add("c3100");
				hashSet9.Add("c4950");
				Dictionary<string, AnnotationData.EnemyLocArea> dictionary19 = new Dictionary<string, AnnotationData.EnemyLocArea>();
				Dictionary<string, string> dictionary20 = new Dictionary<string, string>();
				Dictionary<int, string> groupAreas = new Dictionary<int, string>();
				Dictionary<string, string> dictionary21 = new Dictionary<string, string>();
				AnnotationData.FogLocations locations = ann.Locations;
				foreach (AnnotationData.EnemyLocArea enemyLocArea in (((locations != null) ? locations.EnemyAreas : null) ?? new List<AnnotationData.EnemyLocArea>()))
				{
					dictionary19[enemyLocArea.Name] = enemyLocArea;
					GameDataWriterE.<Write>g__setMapVars|1_163<string>(dictionary20, enemyLocArea.Name, enemyLocArea.MainMap, (string x) => x);
					GameDataWriterE.<Write>g__setMapVars|1_163<int>(groupAreas, enemyLocArea.Name, enemyLocArea.Groups, new Func<string, int>(int.Parse));
					GameDataWriterE.<Write>g__setMapVars|1_163<string>(dictionary21, enemyLocArea.Name, enemyLocArea.Cols, (string x) => x);
				}
				Dictionary<int, ValueTuple<int, int>> dictionary22 = (from r in CS$<>8__locals1.Params["GameAreaParam"].Rows
				select r.ID).Distinct<int>().ToDictionary((int r) => r, (int r) => new ValueTuple<int, int>(-1, -1));
				Predicate<uint> <>9__172;
				foreach (KeyValuePair<string, MSBE> keyValuePair4 in CS$<>8__locals1.msbs)
				{
					MSBE value5 = keyValuePair4.Value;
					HashSet<string> hashSet10 = new HashSet<string>(from e in value5.Events.Generators.SelectMany((MSBE.Event.Generator g) => g.SpawnPartNames)
					where e != null
					select e);
					foreach (MSBE.Part.Enemy enemy5 in value5.Parts.Enemies)
					{
						if (!hashSet4.Contains(enemy5.ModelName) && enemy5.EntityID != 11100766U && enemy5.EntityID != 1050400800U)
						{
							AnnotationData.EnemyLoc enemyLoc;
							string actualArea;
							if (dictionary17.TryGetValue(new ValueTuple<string, string>(keyValuePair4.Key, enemy5.Name), out enemyLoc))
							{
								actualArea = enemyLoc.ActualArea;
							}
							else
							{
								uint[] entityGroupIDs = enemy5.EntityGroupIDs;
								Predicate<uint> match5;
								if ((match5 = <>9__172) == null)
								{
									match5 = (<>9__172 = ((uint g) => groupAreas.ContainsKey((int)g)));
								}
								uint num39 = Array.Find<uint>(entityGroupIDs, match5);
								string text22 = (enemy5.CollisionPartName == null) ? null : (keyValuePair4.Key + "_" + enemy5.CollisionPartName);
								if ((num39 == 0U || !groupAreas.TryGetValue((int)num39, out actualArea)) && (text22 == null || !dictionary21.TryGetValue(text22, out actualArea)) && !dictionary20.TryGetValue(keyValuePair4.Key, out actualArea))
								{
									num34++;
									continue;
								}
							}
							AnnotationData.EnemyLocArea enemyLocArea2 = dictionary19[actualArea];
							int num40 = -1;
							int num41;
							if (dictionary18.TryGetValue(enemy5.NPCParamID, out num41) && num41 > 0)
							{
								int num42;
								if (dictionary14.TryGetValue(num41, out num42))
								{
									num40 = num42;
								}
							}
							else if (enemy5.ModelName != "c0000")
							{
								num40 = 1;
							}
							else if (hashSet8.Contains(enemy5.CharaInitID))
							{
								num40 = 11;
							}
							if (num40 == -1)
							{
								num35++;
								num40 = enemyLocArea2.ScalingTier;
							}
							int num43;
							if (!CS$<>8__locals1.g.AreaTiers.TryGetValue(actualArea, out num43))
							{
								if (!flag6)
								{
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(16, 3);
									defaultInterpolatedStringHandler.AppendLiteral("No tier for ");
									defaultInterpolatedStringHandler.AppendFormatted(actualArea);
									defaultInterpolatedStringHandler.AppendLiteral(" (");
									defaultInterpolatedStringHandler.AppendFormatted(enemyLoc.Map);
									defaultInterpolatedStringHandler.AppendLiteral(" ");
									defaultInterpolatedStringHandler.AppendFormatted(enemyLoc.ID);
									defaultInterpolatedStringHandler.AppendLiteral(")");
									throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
								}
							}
							else
							{
								if (enemy5.EntityID == 0U)
								{
									CS$<>8__locals1.writeMsbs.Add(keyValuePair4.Key);
									enemy5.EntityID = num32++;
									if (num32 % 10000U == 5000U)
									{
										num32 += 5000U;
									}
								}
								int entityID3 = (int)enemy5.EntityID;
								if (dictionary22.ContainsKey(entityID3))
								{
									dictionary22[entityID3] = new ValueTuple<int, int>(enemyLocArea2.ScalingTier, num43);
								}
								if (hashSet7.Contains(enemy5.NPCParamID) || (hashSet6.Contains(enemy5.ModelName) && num43 > num40))
								{
									num43 = num40;
								}
								if (flag13)
								{
									if (num43 >= 29)
									{
										num43 = 22;
									}
									else if (num43 >= 21)
									{
										num43 = 21;
									}
								}
								bool flag14 = CS$<>8__locals1.g.Areas[actualArea].DefeatFlag > 0;
								int num44 = -1;
								if (num40 == num43)
								{
									num37++;
								}
								else
								{
									EldenScaling.AreaScalingValue areaScalingValue = spEffectValues.Areas[new ValueTuple<int, int>(num40, num43)];
									num44 = (flag14 ? areaScalingValue.UniqueFixedScaling : areaScalingValue.RegularScaling);
								}
								string text23 = "scale";
								if (flag14 || hashSet10.Contains(enemy5.Name))
								{
									text23 = "scale2";
								}
								EMEVD.Instruction instruction3;
								if (dictionary15.TryGetValue(entityID3, out instruction3))
								{
									if (num44 == -1)
									{
										text23 = "scale";
									}
									List<object> list22 = new List<object>
									{
										0,
										CS$<>8__locals1.customEvents[text23].ID,
										entityID3,
										num44
									};
									instruction3.PackArgs(list22, false);
									List<EMEVD.Instruction> list23;
									if (dictionary16.TryGetValue(entityID3, out list23))
									{
										foreach (EMEVD.Instruction instruction4 in list23)
										{
											instruction4.PackArgs(list22, false);
										}
									}
									num38++;
								}
								else
								{
									string text24 = CS$<>8__locals1.<Write>g__getEventMap|14(keyValuePair4.Key, enemy5.Name);
									if (!CS$<>8__locals1.emevds.ContainsKey(text24))
									{
										if (!text24.EndsWith("_10"))
										{
											num36++;
										}
									}
									else
									{
										CS$<>8__locals1.<Write>g__addCommonFuncInit|47(text23, text24, new List<object>
										{
											enemy5.EntityID,
											num44
										}, 0);
										num38++;
									}
								}
							}
						}
					}
				}
				foreach (PARAM.Row row8 in CS$<>8__locals1.Params["GameAreaParam"].Rows)
				{
					GameDataWriterE.<>c__DisplayClass1_24 CS$<>8__locals21;
					CS$<>8__locals21.row = row8;
					ValueTuple<int, int> valueTuple3;
					if (dictionary22.TryGetValue(CS$<>8__locals21.row.ID, out valueTuple3))
					{
						ValueTuple<int, int> valueTuple4 = valueTuple3;
						int item6 = valueTuple4.Item1;
						int item7 = valueTuple4.Item2;
						if (item6 != item7 && item6 > 0 && item7 > 0)
						{
							GameDataWriterE.<>c__DisplayClass1_25 CS$<>8__locals22;
							CS$<>8__locals22.mult = EldenScaling.EldenSoulScaling[item7 - 1] / EldenScaling.EldenSoulScaling[item6 - 1];
							GameDataWriterE.<Write>g__applyMult|1_173("bonusSoul_single", ref CS$<>8__locals21, ref CS$<>8__locals22);
							GameDataWriterE.<Write>g__applyMult|1_173("bonusSoul_multi", ref CS$<>8__locals21, ref CS$<>8__locals22);
						}
					}
				}
			}
			HashSet<string> hashSet11 = new HashSet<string>
			{
				"c4190",
				"c4191",
				"c4192",
				"c3160",
				"c4060",
				"c4361",
				"c4362",
				"c4363",
				"c4364",
				"c4365",
				"c2271",
				"c2273",
				"c2275",
				"c2277",
				"c6001",
				"c6010",
				"c6040",
				"c6050",
				"c6060",
				"c6070",
				"c6080",
				"c6081",
				"c6082",
				"c6090",
				"c6100",
				"c4430"
			};
			if (CS$<>8__locals1.opt["nohit"])
			{
				HashSet<int> hashSet12 = new HashSet<int>();
				foreach (KeyValuePair<string, MSBE> keyValuePair5 in CS$<>8__locals1.msbs)
				{
					foreach (MSBE.Part.Enemy enemy6 in keyValuePair5.Value.Parts.Enemies)
					{
						if (!hashSet4.Contains(enemy6.ModelName))
						{
							if (hashSet11.Contains(enemy6.ModelName))
							{
								hashSet12.Add(enemy6.NPCParamID);
							}
							else
							{
								if (enemy6.EntityID == 0U)
								{
									CS$<>8__locals1.writeMsbs.Add(keyValuePair5.Key);
									enemy6.EntityID = num32++;
									if (num32 % 10000U == 5000U)
									{
										num32 += 5000U;
									}
								}
								uint entityID4 = enemy6.EntityID;
								string text25 = CS$<>8__locals1.<Write>g__getEventMap|14(keyValuePair5.Key, enemy6.Name);
								if (CS$<>8__locals1.emevds.ContainsKey(text25))
								{
									CS$<>8__locals1.<Write>g__addCommonFuncInit|47("restart_kill", text25, new List<object>
									{
										enemy6.EntityID
									}, 0);
								}
							}
						}
					}
				}
				foreach (PARAM.Row row9 in CS$<>8__locals1.Params["NpcParam"].Rows)
				{
					if (hashSet12.Contains(row9.ID))
					{
						row9["getSoul"].Value = 0U;
					}
				}
			}
			HashSet<string> hashSet13 = new HashSet<string>(from a in CS$<>8__locals1.g.Areas.Values
			where a.DefeatFlag > 0
			select a.Name);
			Dictionary<string, List<AnnotationData.EnemyLoc>> dictionary23 = new Dictionary<string, List<AnnotationData.EnemyLoc>>();
			foreach (AnnotationData.EnemyLoc enemyLoc2 in ann.Locations.Enemies)
			{
				string actualArea2 = enemyLoc2.ActualArea;
				if (hashSet13.Contains(actualArea2))
				{
					Util.AddMulti<string, AnnotationData.EnemyLoc>(dictionary23, actualArea2, enemyLoc2);
				}
			}
			CS$<>8__locals1.bossEnemies = new Dictionary<string, ValueTuple<string, MSBE.Part.Enemy, Vector3>>();
			foreach (KeyValuePair<string, List<AnnotationData.EnemyLoc>> keyValuePair6 in dictionary23)
			{
				string text18;
				List<AnnotationData.EnemyLoc> list24;
				keyValuePair6.Deconstruct(out text18, out list24);
				string key3 = text18;
				List<AnnotationData.EnemyLoc> list25 = list24;
				AnnotationData.Area area = CS$<>8__locals1.g.Areas[key3];
				AnnotationData.EnemyLoc enemyLoc3 = list25.Find(delegate(AnnotationData.EnemyLoc l)
				{
					MSBE.Part.Enemy enemy12;
					return CS$<>8__locals1.<Write>g__findEnemy|53(l, out enemy12) && (ulong)enemy12.EntityID == (ulong)((long)area.DefeatFlag) && enemy12.EntityID > 0U;
				});
				if (enemyLoc3 == null)
				{
					List<AnnotationData.EnemyLoc> list26 = list25;
					Predicate<AnnotationData.EnemyLoc> match6;
					if ((match6 = CS$<>8__locals1.<>9__176) == null)
					{
						match6 = (CS$<>8__locals1.<>9__176 = delegate(AnnotationData.EnemyLoc l)
						{
							MSBE.Part.Enemy enemy12;
							return base.<Write>g__findEnemy|53(l, out enemy12);
						});
					}
					enemyLoc3 = list26.Find(match6);
				}
				if (enemyLoc3 == null)
				{
					enemyLoc3 = list25[0];
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(61, 3);
					defaultInterpolatedStringHandler.AppendLiteral("Boss enemy ");
					defaultInterpolatedStringHandler.AppendFormatted(area.Text);
					defaultInterpolatedStringHandler.AppendLiteral(" (e.g. ");
					defaultInterpolatedStringHandler.AppendFormatted(enemyLoc3.Map);
					defaultInterpolatedStringHandler.AppendLiteral(" ");
					defaultInterpolatedStringHandler.AppendFormatted(enemyLoc3.ID);
					defaultInterpolatedStringHandler.AppendLiteral(" not found, can't add features based on it");
					Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				else
				{
					MSBE.Part.Enemy enemy7;
					CS$<>8__locals1.<Write>g__findEnemy|53(enemyLoc3, out enemy7);
					Vector3 item8 = enemy7.Position;
					string bossPos = CS$<>8__locals1.g.Areas[key3].BossPos;
					if (bossPos != null)
					{
						string[] array13 = bossPos.Split(' ', StringSplitOptions.None);
						string fromArea = array13[0];
						List<float> list27 = GameDataWriterE.<Write>g__parseFloats|1_20(array13.Skip(1));
						item8 = new Vector3(list27[0], list27[1], list27[2]) + CS$<>8__locals1.<Write>g__getMapOffset|28(fromArea, enemyLoc3.Map);
					}
					CS$<>8__locals1.bossEnemies[key3] = new ValueTuple<string, MSBE.Part.Enemy, Vector3>(enemyLoc3.Map, enemy7, item8);
				}
			}
			CS$<>8__locals1.Params["ActionButtonParam"][10000]["height"].Value = 2f;
			CS$<>8__locals1.Params["ActionButtonParam"][10000]["baseHeightOffset"].Value = -1f;
			CS$<>8__locals1.Params["ActionButtonParam"][9290]["height"].Value = 2f;
			CS$<>8__locals1.Params["ActionButtonParam"][9290]["baseHeightOffset"].Value = -1f;
			CS$<>8__locals1.Params["ActionButtonParam"][1050]["invalidFlag"].Value = 6001U;
			foreach (KeyValuePair<int, EventConfig.FogEdit> keyValuePair7 in CS$<>8__locals1.edits.FogEdits)
			{
				int id = keyValuePair7.Key;
				EventConfig.FogEdit value6 = keyValuePair7.Value;
				if (ann.Entrances.Find((AnnotationData.Entrance e) => e.ID == id) == null)
				{
					if (!ann.Warps.Any((AnnotationData.Entrance e) => e.ID == id))
					{
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(17, 1);
						defaultInterpolatedStringHandler.AppendLiteral("Unknown fog edit ");
						defaultInterpolatedStringHandler.AppendFormatted<int>(id);
						throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
					}
				}
				else
				{
					if (id <= 0)
					{
						throw new Exception("0 id has edit");
					}
					string text26 = CS$<>8__locals1.ownerMap[id];
					EMEVD emevd = CS$<>8__locals1.emevds[text26];
					if (value6.CreateSfx)
					{
						int sfx = value6.Sfx;
						if (id == 1052531802)
						{
							text26 = "m60_52_52_00";
						}
						CS$<>8__locals1.<Write>g__addCommonFuncInit|47("showsfx", text26, new List<object>
						{
							id,
							sfx
						}, 0);
					}
					if (value6.FlagEdits.Count > 0)
					{
						foreach (EventConfig.FlagEdit flagEdit in value6.FlagEdits)
						{
							CS$<>8__locals1.<Write>g__addCommonFuncInit|47("startboss", text26, new List<object>
							{
								flagEdit.SetFlagIf,
								flagEdit.SetFlagArea,
								flagEdit.SetFlag
							}, 0);
						}
					}
				}
			}
			foreach (KeyValuePair<string, List<ValueTuple<string, int>>> keyValuePair8 in dictionary)
			{
				AnnotationData.Area area4 = CS$<>8__locals1.g.Areas[keyValuePair8.Key];
				int defeatFlag = area4.DefeatFlag;
				int bossTrigger = area4.BossTrigger;
				if (defeatFlag == 0 || bossTrigger == 0)
				{
					throw new Exception("Internal error: no boss info for new entrance to " + keyValuePair8.Key);
				}
				foreach (ValueTuple<string, int> valueTuple5 in keyValuePair8.Value)
				{
					string item9 = valueTuple5.Item1;
					int item10 = valueTuple5.Item2;
					CS$<>8__locals1.<Write>g__addCommonFuncInit|47("startboss", item9, new List<object>
					{
						defeatFlag,
						item10,
						bossTrigger
					}, 0);
				}
			}
			foreach (KeyValuePair<int, ValueTuple<string, int>> keyValuePair9 in dictionary4)
			{
				int key4 = keyValuePair9.Key;
				ValueTuple<string, int> value7 = keyValuePair9.Value;
				string item11 = value7.Item1;
				int item12 = value7.Item2;
				AnnotationData.Side side4;
				if (dictionary7.TryGetValue(key4.ToString(), out side4))
				{
					uint value8 = num10++;
					uint value9 = num10++;
					List<string> list28 = new List<string>();
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
					defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value8);
					defaultInterpolatedStringHandler.AppendLiteral(", OFF)");
					list28.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
					defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value9);
					defaultInterpolatedStringHandler.AppendLiteral(", OFF)");
					list28.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(38, 1);
					defaultInterpolatedStringHandler.AppendLiteral("CreateAssetfollowingSFX(");
					defaultInterpolatedStringHandler.AppendFormatted<int>(item12);
					defaultInterpolatedStringHandler.AppendLiteral(", 200, 806870)");
					list28.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(34, 1);
					defaultInterpolatedStringHandler.AppendLiteral("IfActionButtonInArea(MAIN, 9140, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(item12);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					list28.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(99, 4);
					defaultInterpolatedStringHandler.AppendLiteral("DisplayGenericDialogAndSetEventFlags(4300, PromptType.YESNO, NumberofOptions.TwoButtons, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(item12);
					defaultInterpolatedStringHandler.AppendLiteral(", 3, ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value8);
					defaultInterpolatedStringHandler.AppendLiteral(", ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value9);
					defaultInterpolatedStringHandler.AppendLiteral(", ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value9);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					list28.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 1);
					defaultInterpolatedStringHandler.AppendLiteral("GotoIfEventFlag(Label.Label6, ON, TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value8);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					list28.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					list28.Add("WaitFixedTimeSeconds(1)");
					list28.Add("EndUnconditionally(EventEndType.Restart)");
					list28.Add("Label6()");
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(34, 1);
					defaultInterpolatedStringHandler.AppendLiteral("RotateCharacter(10000, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(item12);
					defaultInterpolatedStringHandler.AppendLiteral(", -1, true)");
					list28.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					list28.Add("ForceAnimationPlayback(10000, 60490, false, false, false, ComparisonType.Equal, 1)");
					list28.Add("WaitFixedTimeSeconds(3)");
					List<string> list29 = list28;
					list29.AddRange(CS$<>8__locals1.<Write>g__warpToSide|38(side4));
					list29.AddRange(new string[]
					{
						"EndUnconditionally(EventEndType.Restart)"
					});
					CS$<>8__locals1.<Write>g__addManualInit|49(item11, 0, list29);
					CS$<>8__locals1.edits.UsedWarps.Add(key4.ToString());
				}
			}
			foreach (KeyValuePair<int, ValueTuple<string, int>> keyValuePair10 in dictionary5)
			{
				int key5 = keyValuePair10.Key;
				ValueTuple<string, int> value10 = keyValuePair10.Value;
				string item13 = value10.Item1;
				int item14 = value10.Item2;
				AnnotationData.Side side5;
				if (dictionary7.TryGetValue(key5.ToString(), out side5))
				{
					uint value11 = num10++;
					uint value12 = num10++;
					List<string> list30 = new List<string>();
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
					defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value11);
					defaultInterpolatedStringHandler.AppendLiteral(", OFF)");
					list30.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
					defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value12);
					defaultInterpolatedStringHandler.AppendLiteral(", OFF)");
					list30.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(73, 1);
					defaultInterpolatedStringHandler.AppendLiteral("ForceAnimationPlayback(");
					defaultInterpolatedStringHandler.AppendFormatted<int>(item14);
					defaultInterpolatedStringHandler.AppendLiteral(", 10, true, false, false, ComparisonType.Equal, 1)");
					list30.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 1);
					defaultInterpolatedStringHandler.AppendLiteral("IfActionButtonInArea(AND_01, 9230, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(item14);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					list30.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					list30.Add("IfConditionGroup(MAIN, PASS, AND_01)");
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(100, 4);
					defaultInterpolatedStringHandler.AppendLiteral("DisplayGenericDialogAndSetEventFlags(20300, PromptType.YESNO, NumberofOptions.TwoButtons, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(item14);
					defaultInterpolatedStringHandler.AppendLiteral(", 3, ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value11);
					defaultInterpolatedStringHandler.AppendLiteral(", ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value12);
					defaultInterpolatedStringHandler.AppendLiteral(", ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value12);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					list30.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(73, 1);
					defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.Restart, ON, TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(value12);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					list30.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					list30.Add("SetSpEffect(10000, 514)");
					list30.Add("WaitFixedTimeFrames(1)");
					list30.Add("ForceAnimationPlayback(10000, 60450, false, false, false, ComparisonType.Equal, 1)");
					list30.Add("WaitFixedTimeSeconds(1.5)");
					List<string> list31 = list30;
					list31.AddRange(CS$<>8__locals1.<Write>g__warpToSide|38(side5));
					CS$<>8__locals1.<Write>g__addManualInit|49(item13, 0, list31);
					CS$<>8__locals1.edits.UsedWarps.Add(key5.ToString());
				}
			}
			foreach (AnnotationData.Entrance entrance4 in list5)
			{
				AnnotationData.Side side6;
				if (dictionary7.TryGetValue(entrance4.Name, out side6))
				{
					AnnotationData.Side side = entrance4.ASide;
					AnnotationData.Area area5 = CS$<>8__locals1.g.Areas[side.Area];
					int warpDefeatFlag;
					string text27;
					MSBE.Part.Asset asset16;
					if (side.WarpBonfire > 0)
					{
						if (!CS$<>8__locals1.edits.HiddenBonfires.TryGetValue(side.WarpBonfire, out warpDefeatFlag))
						{
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(59, 2);
							defaultInterpolatedStringHandler.AppendLiteral("No shown bonfire found for ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(side.WarpBonfire);
							defaultInterpolatedStringHandler.AppendLiteral(" ");
							defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Side>(side);
							defaultInterpolatedStringHandler.AppendLiteral(", can't add portal warp from it");
							Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
							continue;
						}
						text27 = entrance4.Area;
						byte[] array14 = GameDataWriterE.<Write>g__parseMap|1_32(text27);
						if ((array14[0] == 60 || array14[0] == 61) && array14[3] == 0)
						{
							text27 = GameDataWriterE.<Write>g__parentMap|1_34(array14, true);
						}
						MSBE.Part.Asset asset15 = CS$<>8__locals1.msbs[text27].Parts.Assets.Find((MSBE.Part.Asset a) => (ulong)a.EntityID == (ulong)((long)(side.WarpBonfire + 1000)));
						if (asset15 == null)
						{
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(60, 2);
							defaultInterpolatedStringHandler.AppendLiteral("Bonfire asset not found for ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(side.WarpBonfire);
							defaultInterpolatedStringHandler.AppendLiteral(" ");
							defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Side>(side);
							defaultInterpolatedStringHandler.AppendLiteral(", can't add portal warp from it");
							Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
							continue;
						}
						asset16 = CS$<>8__locals1.<Write>g__addFakeGate|25(text27, "AEG099_065", asset15.Name, asset15.Position, asset15.Rotation, null);
					}
					else
					{
						if (side.WarpDefeatFlag <= 0)
						{
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(28, 1);
							defaultInterpolatedStringHandler.AppendLiteral("Backportal not possible for ");
							defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Side>(side);
							throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
						}
						warpDefeatFlag = side.WarpDefeatFlag;
						ValueTuple<string, MSBE.Part.Enemy, Vector3> valueTuple6;
						if (!CS$<>8__locals1.bossEnemies.TryGetValue(side.Area, out valueTuple6))
						{
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(62, 2);
							defaultInterpolatedStringHandler.AppendLiteral("Boss enemy data not found for ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(side.WarpDefeatFlag);
							defaultInterpolatedStringHandler.AppendLiteral(" ");
							defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Side>(side);
							defaultInterpolatedStringHandler.AppendLiteral(", can't add portal warp from it");
							Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
							continue;
						}
						ValueTuple<string, MSBE.Part.Enemy, Vector3> valueTuple7 = valueTuple6;
						string item15 = valueTuple7.Item1;
						MSBE.Part.Enemy item16 = valueTuple7.Item2;
						Vector3 item17 = valueTuple7.Item3;
						byte[] array15 = GameDataWriterE.<Write>g__parseMap|1_32(item15);
						if (array15[0] == 60 || array15[0] == 61)
						{
							asset16 = (MSBE.Part.Asset)CS$<>8__locals1.overworldAssetBase.DeepCopy();
							asset16.ModelName = "AEG099_065";
							GameDataWriterE.<Write>g__addAssetModel|1_21(CS$<>8__locals1.msbs[item15], asset16.ModelName);
							GameDataWriterE.<Write>g__setAssetName|1_24(asset16, CS$<>8__locals1.<Write>g__newPartName|12(item15, asset16.ModelName, CS$<>8__locals1.overworldAssetBase.Name));
							if (!(item15 == "m60_38_51_00"))
							{
								item15 == "m60_39_51_00";
							}
							asset16.Position = item17;
							asset16.Rotation = item16.Rotation;
							CS$<>8__locals1.msbs[item15].Parts.Assets.Add(asset16);
							CS$<>8__locals1.writeMsbs.Add(item15);
						}
						else
						{
							string nearby = area5.NearbyAsset;
							if (nearby == null)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(61, 2);
								defaultInterpolatedStringHandler.AppendLiteral("No base asset configured for ");
								defaultInterpolatedStringHandler.AppendFormatted<int>(side.WarpDefeatFlag);
								defaultInterpolatedStringHandler.AppendLiteral(" ");
								defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Side>(side);
								defaultInterpolatedStringHandler.AppendLiteral(", can't add portal warp from it");
								Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
								continue;
							}
							MSBE.Part.Asset asset17 = CS$<>8__locals1.msbs[item15].Parts.Assets.Find((MSBE.Part.Asset a) => a.Name == nearby);
							if (asset17 == null)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(59, 4);
								defaultInterpolatedStringHandler.AppendLiteral("Base asset ");
								defaultInterpolatedStringHandler.AppendFormatted(item15);
								defaultInterpolatedStringHandler.AppendLiteral(" ");
								defaultInterpolatedStringHandler.AppendFormatted(nearby);
								defaultInterpolatedStringHandler.AppendLiteral(" not found for ");
								defaultInterpolatedStringHandler.AppendFormatted<int>(side.WarpBonfire);
								defaultInterpolatedStringHandler.AppendLiteral(" ");
								defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Side>(side);
								defaultInterpolatedStringHandler.AppendLiteral(", can't add portal warp from it");
								Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
								continue;
							}
							asset16 = CS$<>8__locals1.<Write>g__addFakeGate|25(item15, "AEG099_065", asset17.Name, item17, item16.Rotation, null);
						}
						text27 = item15;
					}
					asset16.EntityID = num4++;
					if (side.Warp.Position == null)
					{
						side.Warp.Position = new Vector3?(asset16.Position + CS$<>8__locals1.<Write>g__getMapOffset|28(text27, side.Warp.Map));
					}
					float value13 = (side.WarpBonfire == 13000950) ? 11.8f : 0f;
					bool flag15 = side.WarpDefeatFlag > 0 || area5.BossTrigger == 0 || area5.HasTag("earlyportal");
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 1);
					defaultInterpolatedStringHandler.AppendLiteral("CreateAssetfollowingSFX(");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(asset16.EntityID);
					defaultInterpolatedStringHandler.AppendLiteral(", 190, 1300)");
					string item18 = defaultInterpolatedStringHandler.ToStringAndClear();
					List<string> list32 = new List<string>
					{
						"EndIfPlayerIsInWorldType(EventEndType.End, WorldType.OtherWorld)"
					};
					if (area5.Name == "deeproot_boss")
					{
						list32.AddRange(new string[]
						{
							"WaitFixedTimeSeconds(1)",
							"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, 12032870)"
						});
					}
					if (flag15)
					{
						list32.Add(item18);
					}
					List<string> list33 = list32;
					string[] array16 = new string[4];
					int num45 = 0;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 1);
					defaultInterpolatedStringHandler.AppendLiteral("GotoIfEventFlag(Label.Label0, ON, TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(warpDefeatFlag);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					array16[num45] = defaultInterpolatedStringHandler.ToStringAndClear();
					int num46 = 1;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 1);
					defaultInterpolatedStringHandler.AppendLiteral("IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(warpDefeatFlag);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					array16[num46] = defaultInterpolatedStringHandler.ToStringAndClear();
					int num47 = 2;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 1);
					defaultInterpolatedStringHandler.AppendLiteral("WaitFixedTimeSeconds(");
					defaultInterpolatedStringHandler.AppendFormatted<float>(value13);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					array16[num47] = defaultInterpolatedStringHandler.ToStringAndClear();
					array16[3] = "Label0()";
					list33.AddRange(array16);
					if (!flag15)
					{
						list32.Add(item18);
					}
					List<string> list34 = list32;
					string[] array17 = new string[4];
					int num48 = 0;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 1);
					defaultInterpolatedStringHandler.AppendLiteral("IfActionButtonInArea(AND_02, 9290, ");
					defaultInterpolatedStringHandler.AppendFormatted<uint>(asset16.EntityID);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					array17[num48] = defaultInterpolatedStringHandler.ToStringAndClear();
					array17[1] = "IfConditionGroup(MAIN, PASS, AND_02)";
					array17[2] = "ForceAnimationPlayback(10000, 60460, false, false, false, ComparisonType.Equal, 1)";
					array17[3] = "WaitFixedTimeSeconds(2.5)";
					list34.AddRange(array17);
					list32.AddRange(CS$<>8__locals1.<Write>g__warpToSide|38(side6));
					CS$<>8__locals1.<Write>g__addManualInit|49(entrance4.Area, 0, list32);
					CS$<>8__locals1.edits.UsedWarps.Add(entrance4.Name);
				}
			}
			foreach (AnnotationData.Entrance entrance5 in list6)
			{
				AnnotationData.Side side7;
				if (dictionary7.TryGetValue(entrance5.Name, out side7))
				{
					AnnotationData.Side aside2 = entrance5.ASide;
					string area6 = entrance5.Area;
					string[] array18 = aside2.WarpChest.Split(' ', StringSplitOptions.None);
					int num49 = int.Parse(array18[0]);
					string chestName = array18[1];
					MSBE.Part.Asset chest = CS$<>8__locals1.msbs[area6].Parts.Assets.Find((MSBE.Part.Asset a) => a.Name == chestName);
					if (chest == null)
					{
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(52, 4);
						defaultInterpolatedStringHandler.AppendLiteral("Chest ");
						defaultInterpolatedStringHandler.AppendFormatted(area6);
						defaultInterpolatedStringHandler.AppendLiteral(" ");
						defaultInterpolatedStringHandler.AppendFormatted(chestName);
						defaultInterpolatedStringHandler.AppendLiteral(" #");
						defaultInterpolatedStringHandler.AppendFormatted<int>(num49);
						defaultInterpolatedStringHandler.AppendLiteral(" not found for ");
						defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Side>(aside2);
						defaultInterpolatedStringHandler.AppendLiteral(", can't add new warp from it");
						Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
					}
					else
					{
						if (aside2.Warp.Position == null)
						{
							aside2.Warp.Position = new Vector3?(chest.Position + CS$<>8__locals1.<Write>g__getMapOffset|28(area6, aside2.Warp.Map));
						}
						if (chest.EntityID == 0U)
						{
							chest.EntityID = (uint)num49;
						}
						CS$<>8__locals1.msbs[area6].Events.Treasures.RemoveAll((MSBE.Event.Treasure t) => t.TreasurePartName == chest.Name);
						MSBE.Event.ObjAct objAct5 = CS$<>8__locals1.msbs[area6].Events.ObjActs.Find((MSBE.Event.ObjAct o) => o.ObjActPartName == chest.Name);
						if (objAct5.EventFlagID == 0U)
						{
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(55, 4);
							defaultInterpolatedStringHandler.AppendLiteral("Chest ");
							defaultInterpolatedStringHandler.AppendFormatted(area6);
							defaultInterpolatedStringHandler.AppendLiteral(" ");
							defaultInterpolatedStringHandler.AppendFormatted(chestName);
							defaultInterpolatedStringHandler.AppendLiteral(" #");
							defaultInterpolatedStringHandler.AppendFormatted<int>(num49);
							defaultInterpolatedStringHandler.AppendLiteral(" missing flag for ");
							defaultInterpolatedStringHandler.AppendFormatted<AnnotationData.Side>(aside2);
							defaultInterpolatedStringHandler.AppendLiteral(", can't add new warp from it");
							Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
						}
						else
						{
							objAct5.EventFlagID = 0U;
							objAct5.ObjActEntityID = num4++;
							MSBE.Region.Other other3 = new MSBE.Region.Other
							{
								Name = chest.Name + " region",
								Shape = new MSB.Shape.Cylinder
								{
									Radius = 1f,
									Height = 1.5f
								},
								Position = GameDataWriterE.<Write>g__moveInDirection|1_29(chest.Position, chest.Rotation, -0.5f) - new Vector3(0f, 0.2f, 0f),
								Rotation = chest.Rotation
							};
							other3.EntityID = num4++;
							CS$<>8__locals1.msbs[area6].Regions.Add(other3);
							CS$<>8__locals1.writeMsbs.Add(area6);
							int value14 = (chest.ModelName == "AEG099_630") ? 90 : 100;
							List<string> list35 = new List<string>();
							list35.Add("SetNetworkSyncState(Disabled)");
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(25, 1);
							defaultInterpolatedStringHandler.AppendLiteral("IfObjactEventFlag(MAIN, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(objAct5.ObjActEntityID);
							defaultInterpolatedStringHandler.AppendLiteral(")");
							list35.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 2);
							defaultInterpolatedStringHandler.AppendLiteral("SpawnOneshotSFX(TargetEntityType.Asset, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(chest.EntityID);
							defaultInterpolatedStringHandler.AppendLiteral(", ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(value14);
							defaultInterpolatedStringHandler.AppendLiteral(", 806881)");
							list35.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 2);
							defaultInterpolatedStringHandler.AppendLiteral("SpawnOneshotSFX(TargetEntityType.Asset, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(chest.EntityID);
							defaultInterpolatedStringHandler.AppendLiteral(", ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(value14);
							defaultInterpolatedStringHandler.AppendLiteral(", 806882)");
							list35.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							list35.Add("WaitFixedTimeSeconds(1.3)");
							list35.Add("WaitFixedTimeSeconds(0.9)");
							list35.Add("IfCharacterHPValue(AND_01, 10000, ComparisonType.Equal, 0, ComparisonType.Equal, 1)");
							list35.Add("GotoIfConditionGroupStateUncompiled(Label.Label20, PASS, AND_01)");
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(74, 1);
							defaultInterpolatedStringHandler.AppendLiteral("GotoIfInoutsideArea(Label.Label20, InsideOutsideState.Outside, 10000, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(other3.EntityID);
							defaultInterpolatedStringHandler.AppendLiteral(", 1)");
							list35.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							list35.Add("FadeToBlack(0, 0, true, -1)");
							list35.Add("DisplayGenericDialog(20700, PromptType.YESNO, NumberofOptions.NoButtons, 0, 5)");
							list35.Add("WaitFixedTimeSeconds(0.7)");
							list35.Add("SetSpEffect(10000, 4090)");
							list35.Add("PlaySE(10000, SoundType.CharacterMotion, 8700)");
							list35.Add("WaitFixedTimeSeconds(2.7)");
							list35.Add("IfCharacterHPValue(AND_02, 10000, ComparisonType.Equal, 0, ComparisonType.Equal, 1)");
							list35.Add("GotoIfConditionGroupStateUncompiled(Label.Label18, PASS, AND_02)");
							list35.Add("ChangeCharacterEnableState(10000, Disabled)");
							List<string> list36 = list35;
							list36.AddRange(CS$<>8__locals1.<Write>g__warpToSide|38(side7));
							List<string> list37 = list36;
							string[] array19 = new string[14];
							array19[0] = "WaitFixedTimeSeconds(3)";
							array19[1] = "SetSpEffect(10000, 4091)";
							array19[2] = "ChangeCharacterEnableState(10000, Enabled)";
							array19[3] = "FadeToBlack(0, 0, false, -1)";
							array19[4] = "GotoUnconditionally(Label.Label19)";
							array19[5] = "Label20()";
							array19[6] = "WaitFixedTimeSeconds(3.4)";
							array19[7] = "Label18()";
							array19[8] = "WaitFixedTimeSeconds(1)";
							array19[9] = "Label19()";
							int num50 = 10;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(72, 1);
							defaultInterpolatedStringHandler.AppendLiteral("ForceAnimationPlayback(");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(chest.EntityID);
							defaultInterpolatedStringHandler.AppendLiteral(", 2, false, true, false, ComparisonType.Equal, 1)");
							array19[num50] = defaultInterpolatedStringHandler.ToStringAndClear();
							array19[11] = "SetNetworkSyncState(Enabled)";
							int num51 = 12;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(29, 1);
							defaultInterpolatedStringHandler.AppendLiteral("SetObjactState(");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(chest.EntityID);
							defaultInterpolatedStringHandler.AppendLiteral(", -1, Enabled)");
							array19[num51] = defaultInterpolatedStringHandler.ToStringAndClear();
							array19[13] = "EndUnconditionally(EventEndType.Restart)";
							list37.AddRange(array19);
							CS$<>8__locals1.<Write>g__addManualInit|49(CS$<>8__locals1.<Write>g__getEventMap|14(area6, chest.Name), 0, list36);
							CS$<>8__locals1.edits.UsedWarps.Add(entrance5.Name);
						}
					}
				}
			}
			foreach (AnnotationData.Entrance entrance6 in list4)
			{
				AnnotationData.Side side8;
				if (dictionary7.TryGetValue(entrance6.Name, out side8))
				{
					AnnotationData.Side aside3 = entrance6.ASide;
					Graph.WarpPoint warp3 = aside3.Warp;
					CS$<>8__locals1.<Write>g__addCommonFuncInit|47("sitflag", warp3.Map, new List<object>
					{
						aside3.WarpBonfire,
						warp3.SitFlag
					}, 0);
					if (entrance6.HasTag("gracewarp"))
					{
						CS$<>8__locals1.edits.UsedWarps.Add(entrance6.Name);
					}
					if (aside3.Warp.Position == null)
					{
						Vector3 value15;
						Vector3 vector16;
						if (CS$<>8__locals1.<Write>g__getAssetLocation|31(entrance6.Area, aside3.WarpBonfire + 1000, out value15, out vector16))
						{
							aside3.Warp.Position = new Vector3?(value15);
						}
						else
						{
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 3);
							defaultInterpolatedStringHandler.AppendLiteral("Warp bonfire missing position: ");
							defaultInterpolatedStringHandler.AppendFormatted(entrance6.FullName);
							defaultInterpolatedStringHandler.AppendLiteral(", ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(aside3.WarpBonfire + 1000);
							defaultInterpolatedStringHandler.AppendLiteral(" in ");
							defaultInterpolatedStringHandler.AppendFormatted(entrance6.Area);
							Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
						}
					}
				}
			}
			if (flag)
			{
				MSBE.Region.SpawnPoint spawnPoint2 = new MSBE.Region.SpawnPoint();
				spawnPoint2.EntityID = num4++;
				spawnPoint2.Name = "Upper Ensis SpawnPoint";
				spawnPoint2.Position = new Vector3(93.636f, 385.529f, 67.916f);
				spawnPoint2.Rotation = new Vector3(0f, -29.739f, 0f);
				CS$<>8__locals1.msbs["m61_47_44_00"].Regions.Add(spawnPoint2);
				MSBE.Part.Asset asset18 = CS$<>8__locals1.<Write>g__addFakeGate|25("m61_47_44_00", "AEG099_630", "overworld", new Vector3(128.951f, 381.973f, 10.572f), new Vector3(0f, 135f, 0f), null);
				asset18.EntityID = num4++;
				MSBE.Event.ObjAct objAct6 = new MSBE.Event.ObjAct
				{
					MapID = -1,
					UnkS0C = -1,
					UnkE0C = byte.MaxValue,
					Name = "Upper Ensis ObjAct",
					ObjActEntityID = 2047443679U,
					ObjActPartName = asset18.Name,
					ObjActID = 200,
					StateType = 5
				};
				CS$<>8__locals1.msbs["m61_47_44_00"].Events.ObjActs.Add(objAct6);
				MSBE.Part.Asset asset19 = CS$<>8__locals1.msbs["m61_47_44_00"].Parts.Assets.Find((MSBE.Part.Asset e) => e.Name == "AEG463_610_9018");
				if (asset19 != null)
				{
					asset19.Position = new Vector3(32.453f, 347.119f, 88.817f);
					asset19.Rotation = new Vector3(0f, 25f, 0f);
				}
				int value16 = (asset18.ModelName == "AEG099_630") ? 90 : 100;
				List<string> list38 = new List<string>();
				list38.Add("SetEventFlag(TargetEventFlagType.EventFlag, 2047457180, OFF)");
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(25, 1);
				defaultInterpolatedStringHandler.AppendLiteral("IfObjactEventFlag(MAIN, ");
				defaultInterpolatedStringHandler.AppendFormatted<uint>(objAct6.ObjActEntityID);
				defaultInterpolatedStringHandler.AppendLiteral(")");
				list38.Add(defaultInterpolatedStringHandler.ToStringAndClear());
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(30, 1);
				defaultInterpolatedStringHandler.AppendLiteral("SetObjactState(");
				defaultInterpolatedStringHandler.AppendFormatted<uint>(asset18.EntityID);
				defaultInterpolatedStringHandler.AppendLiteral(", -1, Disabled)");
				list38.Add(defaultInterpolatedStringHandler.ToStringAndClear());
				list38.Add("SetNetworkSyncState(Disabled)");
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 2);
				defaultInterpolatedStringHandler.AppendLiteral("SpawnOneshotSFX(TargetEntityType.Asset, ");
				defaultInterpolatedStringHandler.AppendFormatted<uint>(asset18.EntityID);
				defaultInterpolatedStringHandler.AppendLiteral(", ");
				defaultInterpolatedStringHandler.AppendFormatted<int>(value16);
				defaultInterpolatedStringHandler.AppendLiteral(", 806881)");
				list38.Add(defaultInterpolatedStringHandler.ToStringAndClear());
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 2);
				defaultInterpolatedStringHandler.AppendLiteral("SpawnOneshotSFX(TargetEntityType.Asset, ");
				defaultInterpolatedStringHandler.AppendFormatted<uint>(asset18.EntityID);
				defaultInterpolatedStringHandler.AppendLiteral(", ");
				defaultInterpolatedStringHandler.AppendFormatted<int>(value16);
				defaultInterpolatedStringHandler.AppendLiteral(", 806882)");
				list38.Add(defaultInterpolatedStringHandler.ToStringAndClear());
				list38.Add("FadeToBlack(0, 0, true, 0)");
				list38.Add("WaitFixedTimeSeconds(1.3)");
				list38.Add("WaitFixedTimeSeconds(0.9)");
				list38.Add("IfCharacterHPValue(AND_01, 10000, ComparisonType.Equal, 0, ComparisonType.Equal, 1)");
				list38.Add("GotoIfConditionGroupStateUncompiled(Label.Label20, PASS, AND_01)");
				list38.Add("DisplayGenericDialog(20700, PromptType.YESNO, NumberofOptions.NoButtons, 0, 5)");
				list38.Add("WaitFixedTimeSeconds(0.7)");
				list38.Add("SetSpEffect(10000, 4090)");
				list38.Add("PlaySE(10000, SoundType.CharacterMotion, 8700)");
				list38.Add("WaitFixedTimeSeconds(2.7)");
				list38.Add("IfCharacterHPValue(AND_02, 10000, ComparisonType.Equal, 0, ComparisonType.Equal, 1)");
				list38.Add("GotoIfConditionGroupStateUncompiled(Label.Label18, PASS, AND_02)");
				list38.Add("ChangeCharacterEnableState(10000, Disabled)");
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(92, 1);
				defaultInterpolatedStringHandler.AppendLiteral("WarpCharacterAndCopyFloorWithFadeout(10000, TargetEntityType.Area, ");
				defaultInterpolatedStringHandler.AppendFormatted<uint>(spawnPoint2.EntityID);
				defaultInterpolatedStringHandler.AppendLiteral(", -1, 10000, false, true)");
				list38.Add(defaultInterpolatedStringHandler.ToStringAndClear());
				list38.Add("WaitFixedTimeSeconds(1)");
				list38.Add("SetSpEffect(10000, 4091)");
				list38.Add("ChangeCharacterEnableState(10000, Enabled)");
				list38.Add("ForceAnimationPlayback(10000, 60131, false, false, false, ComparisonType.Equal, 1)");
				list38.Add("FadeToBlack(0, 0, false, -1)");
				list38.Add("GotoUnconditionally(Label.Label19)");
				List<string> list39 = list38;
				List<string> list40 = list39;
				string[] array20 = new string[10];
				array20[0] = "Label20()";
				array20[1] = "WaitFixedTimeSeconds(3.4)";
				array20[2] = "Label18()";
				array20[3] = "WaitFixedTimeSeconds(1)";
				array20[4] = "Label19()";
				array20[5] = "FadeToBlack(0, 0, false, -1)";
				int num52 = 6;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(72, 1);
				defaultInterpolatedStringHandler.AppendLiteral("ForceAnimationPlayback(");
				defaultInterpolatedStringHandler.AppendFormatted<uint>(asset18.EntityID);
				defaultInterpolatedStringHandler.AppendLiteral(", 2, false, true, false, ComparisonType.Equal, 1)");
				array20[num52] = defaultInterpolatedStringHandler.ToStringAndClear();
				array20[7] = "SetNetworkSyncState(Enabled)";
				int num53 = 8;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(29, 1);
				defaultInterpolatedStringHandler.AppendLiteral("SetObjactState(");
				defaultInterpolatedStringHandler.AppendFormatted<uint>(asset18.EntityID);
				defaultInterpolatedStringHandler.AppendLiteral(", -1, Enabled)");
				array20[num53] = defaultInterpolatedStringHandler.ToStringAndClear();
				array20[9] = "EndUnconditionally(EventEndType.Restart)";
				list40.AddRange(array20);
				CS$<>8__locals1.<Write>g__addManualInit|49("m61_47_44_00", 0, list39);
				HashSet<string> hashSet14 = new HashSet<string>
				{
					"AEG410_901_3800",
					"AEG410_905_3800"
				};
				foreach (MSBE.Part.Asset asset20 in CS$<>8__locals1.msbs["m20_00_00_00"].Parts.Assets)
				{
					if (asset20.EntityGroupIDs[0] == 20006660U && hashSet14.Contains(asset20.Name))
					{
						asset20.EntityGroupIDs[0] = 20006661U;
					}
				}
				CS$<>8__locals1.<Write>g__addBarrier|185("AEG410_901", new Vector3(-139.676f, -3.592f, 7.895f), new Vector3(0f, -143.022f, 0f), "AEG410_415_1000", 20006662U);
				CS$<>8__locals1.<Write>g__addBarrier|185("AEG410_905", new Vector3(-150.531f, 6.889f, -3.324f), new Vector3(0f, -83.082f, 0f), "AEG410_415_1000", 20006662U);
				CS$<>8__locals1.<Write>g__addManualInit|49("m20_01_00_00", 0, new string[]
				{
					"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, 330)",
					"ChangeAssetEnableState(20006662, Disabled)"
				});
				bool flag16 = CS$<>8__locals1.opt["tierreq"] && num28 > 0U && CS$<>8__locals1.g.ExcludeMode == AnnotationData.AreaMode.Base;
				if (CS$<>8__locals1.opt["bossreqdlc"] || flag16)
				{
					CS$<>8__locals1.<Write>g__addBarrier|185("AEG410_901", new Vector3(-205.939f, 297.455f, -183.685f), new Vector3(0f, -61.701f, 0f), "AEG410_338_4000", 20006663U);
					CS$<>8__locals1.<Write>g__addBarrier|185("AEG410_905", new Vector3(-213.932f, 306.452f, -181.432f), new Vector3(0f, 13.218f, 0f), "AEG410_338_4000", 20006663U);
					MSBE.Part.Asset asset21 = CS$<>8__locals1.<Write>g__addFakeGate|25("m20_01_00_00", "AEG099_090", "AEG410_338_4000", new Vector3(-202.365f, 298.99f, -185.673f), new Vector3(0f, 118.299f, 0f), null);
					asset21.EntityID = num4++;
					int num54 = 666346600;
					CS$<>8__locals1.menuFMGs["EventTextForMap"][num54] = "Defeat a major boss with late DLC scaling to unseal the thorns.";
					List<string> list41 = new List<string>();
					if (CS$<>8__locals1.opt["bossreqdlc"])
					{
						List<AnnotationData.Area> list42 = (from area in CS$<>8__locals1.g.Areas.Values
						where area.DefeatFlag > 0 && area.HasTag("remembrance") && area.HasTag("dlc")
						select area).OrderBy(delegate(AnnotationData.Area area)
						{
							int result;
							if (!CS$<>8__locals1.g.AreaTiers.TryGetValue(area.Name, out result))
							{
								return 999999;
							}
							return result;
						}).ToList<AnnotationData.Area>();
						int num55 = 666349900;
						foreach (AnnotationData.Area area7 in list42)
						{
							string str = area7.Text.Split(" - ", StringSplitOptions.None).Last<string>();
							string text28 = "Claim all other DLC remembrance bosses to unseal the thorns.\nThe domain of " + str + " remains to be cleared.";
							int num56 = num55++;
							CS$<>8__locals1.menuFMGs["EventTextForMap"][num56] = text28;
							List<string> list43 = list41;
							string[] array21 = new string[7];
							int num57 = 0;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 1);
							defaultInterpolatedStringHandler.AppendLiteral("GotoIfEventFlag(Label.Label0, ON, TargetEventFlagType.EventFlag, ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(area7.DefeatFlag);
							defaultInterpolatedStringHandler.AppendLiteral(")");
							array21[num57] = defaultInterpolatedStringHandler.ToStringAndClear();
							int num58 = 1;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 1);
							defaultInterpolatedStringHandler.AppendLiteral("IfActionButtonInArea(MAIN, 209502, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(asset21.EntityID);
							defaultInterpolatedStringHandler.AppendLiteral(")");
							array21[num58] = defaultInterpolatedStringHandler.ToStringAndClear();
							int num59 = 2;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(73, 1);
							defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.Restart, ON, TargetEventFlagType.EventFlag, ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(area7.DefeatFlag);
							defaultInterpolatedStringHandler.AppendLiteral(")");
							array21[num59] = defaultInterpolatedStringHandler.ToStringAndClear();
							int num60 = 3;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(72, 2);
							defaultInterpolatedStringHandler.AppendLiteral("DisplayGenericDialog(");
							defaultInterpolatedStringHandler.AppendFormatted<int>(num56);
							defaultInterpolatedStringHandler.AppendLiteral(", PromptType.YESNO, NumberofOptions.NoButtons, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(asset21.EntityID);
							defaultInterpolatedStringHandler.AppendLiteral(", 3)");
							array21[num60] = defaultInterpolatedStringHandler.ToStringAndClear();
							array21[4] = "WaitFixedTimeSeconds(3)";
							array21[5] = "EndUnconditionally(EventEndType.Restart)";
							array21[6] = "Label0()";
							list43.AddRange(array21);
						}
					}
					if (flag16)
					{
						List<string> list44 = list41;
						string[] array22 = new string[7];
						int num61 = 0;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 1);
						defaultInterpolatedStringHandler.AppendLiteral("GotoIfEventFlag(Label.Label0, ON, TargetEventFlagType.EventFlag, ");
						defaultInterpolatedStringHandler.AppendFormatted<uint>(num28);
						defaultInterpolatedStringHandler.AppendLiteral(")");
						array22[num61] = defaultInterpolatedStringHandler.ToStringAndClear();
						int num62 = 1;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 1);
						defaultInterpolatedStringHandler.AppendLiteral("IfActionButtonInArea(MAIN, 209502, ");
						defaultInterpolatedStringHandler.AppendFormatted<uint>(asset21.EntityID);
						defaultInterpolatedStringHandler.AppendLiteral(")");
						array22[num62] = defaultInterpolatedStringHandler.ToStringAndClear();
						int num63 = 2;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(73, 1);
						defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.Restart, ON, TargetEventFlagType.EventFlag, ");
						defaultInterpolatedStringHandler.AppendFormatted<uint>(num28);
						defaultInterpolatedStringHandler.AppendLiteral(")");
						array22[num63] = defaultInterpolatedStringHandler.ToStringAndClear();
						int num64 = 3;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(72, 2);
						defaultInterpolatedStringHandler.AppendLiteral("DisplayGenericDialog(");
						defaultInterpolatedStringHandler.AppendFormatted<int>(num54);
						defaultInterpolatedStringHandler.AppendLiteral(", PromptType.YESNO, NumberofOptions.NoButtons, ");
						defaultInterpolatedStringHandler.AppendFormatted<uint>(asset21.EntityID);
						defaultInterpolatedStringHandler.AppendLiteral(", 3)");
						array22[num64] = defaultInterpolatedStringHandler.ToStringAndClear();
						array22[4] = "WaitFixedTimeSeconds(3)";
						array22[5] = "EndUnconditionally(EventEndType.Restart)";
						array22[6] = "Label0()";
						list44.AddRange(array22);
					}
					list41.Add("ChangeAssetEnableState(20006663, Disabled)");
					CS$<>8__locals1.<Write>g__addManualInit|49("m20_01_00_00", 0, list41);
				}
			}
			foreach (KeyValuePair<string, List<uint>> keyValuePair11 in dictionary2)
			{
				foreach (uint num65 in keyValuePair11.Value)
				{
					CS$<>8__locals1.<Write>g__addCommonFuncInit|47("setflag", keyValuePair11.Key, new List<object>
					{
						num65
					}, 50);
				}
			}
			foreach (string key6 in new List<string>
			{
				"m60_45_52_10_AEG099_001_9500",
				"m61_44_45_10_AEG099_002_9500",
				"m61_44_45_10_AEG099_002_9501"
			})
			{
				AnnotationData.Entrance entrance7;
				if (CS$<>8__locals1.g.EntranceIds.TryGetValue(key6, out entrance7))
				{
					int id2 = entrance7.ID;
					string text29 = CS$<>8__locals1.ownerMap[id2];
					EMEVD emevd2 = CS$<>8__locals1.emevds[text29];
					EventConfig.NewEvent newEvent2 = CS$<>8__locals1.customEvents["showsfx"];
					emevd2.Events[0].Instructions.Add(new EMEVD.Instruction(2000, 6, new List<object>
					{
						0,
						newEvent2.ID,
						id2,
						5
					}));
					CS$<>8__locals1.writeEmevds.Add(text29);
				}
			}
			AnnotationData.Entrance entrance8;
			if (CS$<>8__locals1.g.EntranceIds.TryGetValue("m61_49_48_00_AEG099_002_9000", out entrance8) && !entrance8.IsFixed)
			{
				MSBE.Region.Other other4 = CS$<>8__locals1.msbs["m61_49_48_00"].Regions.Others.Find((MSBE.Region.Other r) => r.EntityID == 2049482810U);
				if (other4 != null)
				{
					other4.Position = new Vector3(7.696f, 583.175f, 70.72f);
					other4.Rotation = new Vector3(0f, -74.375f, 0f);
					CS$<>8__locals1.writeMsbs.Add("m61_49_48_00");
				}
			}
			AnnotationData.Entrance entrance9;
			if (CS$<>8__locals1.g.EntranceIds.TryGetValue("m22_00_00_00_AEG099_002_3000", out entrance9) && !entrance9.IsFixed)
			{
				float num66 = 0f;
				foreach (MSBE.Part.Enemy enemy8 in CS$<>8__locals1.msbs["m22_00_00_00"].Parts.Enemies)
				{
					if (enemy8.CollisionPartName == "h009000" && enemy8.ModelName == "c5020" && enemy8.Position.Y < -250f && enemy8.Position.X < -125f)
					{
						enemy8.Position = new Vector3(-118.098f, -294.269f, 65.881f);
						num66 += 0.2f;
						CS$<>8__locals1.writeMsbs.Add("m22_00_00_00");
					}
				}
			}
			foreach (Graph.Node node2 in CS$<>8__locals1.g.Nodes.Values)
			{
				foreach (Graph.Edge edge2 in node2.To)
				{
					Graph.Edge link2 = edge2.Link;
					if (link2 == null)
					{
						if (!flag6)
						{
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(25, 1);
							defaultInterpolatedStringHandler.AppendLiteral("Internal error: Unlinked ");
							defaultInterpolatedStringHandler.AppendFormatted<Graph.Edge>(edge2);
							throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
						}
					}
					else
					{
						Graph.WarpPoint warp4 = edge2.Side.Warp;
						Graph.WarpPoint warp5 = link2.Side.Warp;
						if (warp4 == null || warp5 == null)
						{
							if (!edge2.IsFixed || !link2.IsFixed)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(26, 4);
								defaultInterpolatedStringHandler.AppendLiteral("Missing warps - ");
								defaultInterpolatedStringHandler.AppendFormatted<bool>(warp4 == null);
								defaultInterpolatedStringHandler.AppendLiteral(" ");
								defaultInterpolatedStringHandler.AppendFormatted<bool>(warp5 == null);
								defaultInterpolatedStringHandler.AppendLiteral(" for ");
								defaultInterpolatedStringHandler.AppendFormatted<Graph.Edge>(edge2);
								defaultInterpolatedStringHandler.AppendLiteral(" -> ");
								defaultInterpolatedStringHandler.AppendFormatted<Graph.Edge>(link2);
								throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
							}
							CS$<>8__locals1.<Write>g__fixMultiplayerGate|56(edge2);
						}
						else if (edge2.Name == link2.Name && edge2.IsFixed && !CS$<>8__locals1.opt["alwaysshow"])
						{
							CS$<>8__locals1.<Write>g__fixMultiplayerGate|56(edge2);
						}
						else
						{
							EventConfig.FogEdit fogEdit = null;
							if (warp4.Action == 0)
							{
								AnnotationData.Entrance entrance10 = CS$<>8__locals1.g.EntranceIds[edge2.Name];
								if (!CS$<>8__locals1.edits.UsedWarps.Contains(entrance10.Name))
								{
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(44, 3);
									defaultInterpolatedStringHandler.AppendLiteral("Did not add warp ");
									defaultInterpolatedStringHandler.AppendFormatted(entrance10.Name);
									defaultInterpolatedStringHandler.AppendLiteral(" [");
									defaultInterpolatedStringHandler.AppendFormatted(entrance10.Tags);
									defaultInterpolatedStringHandler.AppendLiteral("] in events pass (found ");
									defaultInterpolatedStringHandler.AppendFormatted(string.Join(", ", CS$<>8__locals1.edits.UsedWarps));
									defaultInterpolatedStringHandler.AppendLiteral(")");
									throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
								}
								EventConfig.FogEdit fogEdit2;
								if (CS$<>8__locals1.edits.FogEdits.TryGetValue(entrance10.ID, out fogEdit2) && fogEdit.RepeatWarpObject > 0)
								{
									fogEdit = fogEdit2;
								}
								else if (warp4.WarpFlag <= 0)
								{
									continue;
								}
							}
							int region6 = warp5.Region;
							byte[] array23 = GameDataWriterE.<Write>g__parseMap|1_32(warp5.Map);
							int num67 = 0;
							int num68 = 0;
							byte[] array24 = new byte[4];
							if (link2.Side.AlternateSide != null && link2.Side.AlternateFlag != 0)
							{
								if (link2.Side.AlternateFlag > 0)
								{
									num67 = link2.Side.AlternateFlag;
									num68 = link2.Side.AlternateSide.Warp.Region;
									array24 = GameDataWriterE.<Write>g__parseMap|1_32(link2.Side.AlternateSide.Warp.Map);
								}
								else
								{
									num67 = -link2.Side.AlternateFlag;
									num68 = region6;
									array24 = array23;
									region6 = link2.Side.AlternateSide.Warp.Region;
									array23 = GameDataWriterE.<Write>g__parseMap|1_32(link2.Side.AlternateSide.Warp.Map);
								}
							}
							List<AnnotationData.Side> list45 = new List<AnnotationData.Side>
							{
								edge2.Side
							};
							if (edge2.Side.AlternateSide != null)
							{
								list45.Add(edge2.Side.AlternateSide);
							}
							foreach (AnnotationData.Side side9 in list45)
							{
								warp4 = side9.Warp;
								int num69 = (warp4.OtherSide > 0) ? warp4.OtherSide : warp4.Action;
								string map = warp4.Map;
								int num70 = CS$<>8__locals1.<Write>g__getNameFlag|13(side9.BossDefeatName, side9.Area, (AnnotationData.Area ar) => ar.DefeatFlag);
								int num71 = CS$<>8__locals1.<Write>g__getNameFlag|13(side9.BossTrapName, side9.Area, (AnnotationData.Area ar) => ar.TrapFlag);
								if (CS$<>8__locals1.opt["pacifist"] && !side9.HasTag("nopacifist"))
								{
									if (side9.HasTag("musttrap"))
									{
										num71 = CS$<>8__locals1.g.Areas[side9.Area].BossTrigger;
									}
									else
									{
										num70 = 0;
										num71 = 0;
									}
								}
								if (CS$<>8__locals1.opt[Feature.ChapelInit] && side9.HasTag("start"))
								{
									num70 = (int)num8;
								}
								if (CS$<>8__locals1.opt[Feature.NoBossBonfire] && num70 == 11000501)
								{
									num70 = 11000500;
								}
								if (CS$<>8__locals1.opt[Feature.Segmented])
								{
									num71 = 0;
								}
								List<string> list46 = new List<string>();
								string eventMap;
								if (warp4.Action <= 0 && warp4.WarpFlag > 0)
								{
									List<string> list47 = list46;
									string[] array25 = new string[3];
									int num72 = 0;
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 1);
									defaultInterpolatedStringHandler.AppendLiteral("GotoIfEventFlag(Label.Label0, ON, TargetEventFlagType.EventFlag, ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(warp4.WarpFlag);
									defaultInterpolatedStringHandler.AppendLiteral(")");
									array25[num72] = defaultInterpolatedStringHandler.ToStringAndClear();
									int num73 = 1;
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 1);
									defaultInterpolatedStringHandler.AppendLiteral("IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(warp4.WarpFlag);
									defaultInterpolatedStringHandler.AppendLiteral(")");
									array25[num73] = defaultInterpolatedStringHandler.ToStringAndClear();
									array25[2] = "WaitFixedTimeSeconds(0.1)";
									list47.AddRange(array25);
									list46.AddRange(CS$<>8__locals1.<Write>g__warpToSide|38(link2.Side));
									List<string> list48 = list46;
									string[] array26 = new string[3];
									array26[0] = "EndUnconditionally(EventEndType.End)";
									array26[1] = "Label0()";
									int num74 = 2;
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
									defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(warp4.WarpFlag);
									defaultInterpolatedStringHandler.AppendLiteral(", OFF)");
									array26[num74] = defaultInterpolatedStringHandler.ToStringAndClear();
									list48.AddRange(array26);
									eventMap = warp4.Map;
									List<object> list49 = new List<object>();
									list49.Add(warp4.WarpFlag);
									list49.Add(region6);
									list49.Add(array23[0]);
									list49.Add(array23[1]);
									list49.Add(array23[2]);
									list49.Add(array23[3]);
									list49.Add(num67);
									list49.Add(num68);
									list49.Add(array24[0]);
									list49.Add(array24[1]);
									list49.Add(array24[2]);
									list49.Add(array24[3]);
								}
								else
								{
									if (fogEdit != null)
									{
										throw new InvalidOperationException();
									}
									int num75 = 10000;
									eventMap = CS$<>8__locals1.ownerMap[warp4.ID];
									if (num70 > 0)
									{
										if (num71 > 0)
										{
											List<string> list50 = list46;
											string[] array27 = new string[5];
											int num76 = 0;
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(67, 1);
											defaultInterpolatedStringHandler.AppendLiteral("GotoIfEventFlag(Label.Label0, OFF, TargetEventFlagType.EventFlag, ");
											defaultInterpolatedStringHandler.AppendFormatted<int>(num71);
											defaultInterpolatedStringHandler.AppendLiteral(")");
											array27[num76] = defaultInterpolatedStringHandler.ToStringAndClear();
											int num77 = 1;
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 1);
											defaultInterpolatedStringHandler.AppendLiteral("IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, ");
											defaultInterpolatedStringHandler.AppendFormatted<int>(num70);
											defaultInterpolatedStringHandler.AppendLiteral(")");
											array27[num77] = defaultInterpolatedStringHandler.ToStringAndClear();
											array27[2] = "Label0()";
											int num78 = 3;
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 1);
											defaultInterpolatedStringHandler.AppendLiteral("GotoIfEventFlag(Label.Label1, ON, TargetEventFlagType.EventFlag, ");
											defaultInterpolatedStringHandler.AppendFormatted<int>(num70);
											defaultInterpolatedStringHandler.AppendLiteral(")");
											array27[num78] = defaultInterpolatedStringHandler.ToStringAndClear();
											int num79 = 4;
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(55, 1);
											defaultInterpolatedStringHandler.AppendLiteral("IfEventFlag(OR_01, ON, TargetEventFlagType.EventFlag, ");
											defaultInterpolatedStringHandler.AppendFormatted<int>(num71);
											defaultInterpolatedStringHandler.AppendLiteral(")");
											array27[num79] = defaultInterpolatedStringHandler.ToStringAndClear();
											list50.AddRange(array27);
										}
										else
										{
											List<string> list51 = list46;
											string[] array28 = new string[1];
											int num80 = 0;
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 1);
											defaultInterpolatedStringHandler.AppendLiteral("IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, ");
											defaultInterpolatedStringHandler.AppendFormatted<int>(num70);
											defaultInterpolatedStringHandler.AppendLiteral(")");
											array28[num80] = defaultInterpolatedStringHandler.ToStringAndClear();
											list51.AddRange(array28);
										}
									}
									List<string> list52 = list46;
									string[] array29 = new string[13];
									array29[0] = "Label1()";
									int num81 = 1;
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 2);
									defaultInterpolatedStringHandler.AppendLiteral("IfActionButtonInArea(AND_05, ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(num75);
									defaultInterpolatedStringHandler.AppendLiteral(", ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(warp4.Action);
									defaultInterpolatedStringHandler.AppendLiteral(")");
									array29[num81] = defaultInterpolatedStringHandler.ToStringAndClear();
									array29[2] = "IfConditionGroup(OR_01, PASS, AND_05)";
									array29[3] = "IfConditionGroup(MAIN, PASS, OR_01)";
									array29[4] = "EndIfConditionGroupStateCompiled(EventEndType.Restart, FAIL, AND_05)";
									array29[5] = "IfCharacterHasSpEffect(AND_06, 10000, 4280, false, ComparisonType.Equal, 1)";
									array29[6] = "GotoIfConditionGroupStateUncompiled(Label.Label10, PASS, AND_06)";
									int num82 = 7;
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(80, 1);
									defaultInterpolatedStringHandler.AppendLiteral("DisplayGenericDialog(90010, PromptType.OKCANCEL, NumberofOptions.OneButton, ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(warp4.Action);
									defaultInterpolatedStringHandler.AppendLiteral(", 3)");
									array29[num82] = defaultInterpolatedStringHandler.ToStringAndClear();
									array29[8] = "WaitFixedTimeSeconds(1)";
									array29[9] = "EndUnconditionally(EventEndType.Restart)";
									array29[10] = "Label10()";
									int num83 = 11;
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(38, 1);
									defaultInterpolatedStringHandler.AppendLiteral("RotateCharacter(10000, ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(num69);
									defaultInterpolatedStringHandler.AppendLiteral(", 60060, false)");
									array29[num83] = defaultInterpolatedStringHandler.ToStringAndClear();
									array29[12] = "WaitFixedTimeSeconds(1)";
									list52.AddRange(array29);
									list46.AddRange(CS$<>8__locals1.<Write>g__warpToSide|38(link2.Side));
									list46.AddRange(new string[]
									{
										"WaitFixedTimeFrames(1)",
										"EndUnconditionally(EventEndType.Restart)"
									});
									List<object> list53 = new List<object>();
									list53.Add(warp4.Action);
									list53.Add(num75);
									list53.Add(region6);
									list53.Add(array23[0]);
									list53.Add(array23[1]);
									list53.Add(array23[2]);
									list53.Add(array23[3]);
									list53.Add(num70);
									list53.Add(num71);
									list53.Add(num67);
									list53.Add(num68);
									list53.Add(array24[0]);
									list53.Add(array24[1]);
									list53.Add(array24[2]);
									list53.Add(array24[3]);
									list53.Add(num69);
								}
								CS$<>8__locals1.<Write>g__addManualInit|49(eventMap, 0, list46);
							}
						}
					}
				}
			}
			AnnotationData.Side side10;
			if (dictionary7.TryGetValue("20012020", out side10))
			{
				MSBE.Part.Asset asset22 = CS$<>8__locals1.msbs["m61_44_45_10"].Parts.Assets.Find((MSBE.Part.Asset a) => a.Name == "AEG464_038_3000");
				if (asset22 != null)
				{
					asset22.EntityID = 2044451509U;
					CS$<>8__locals1.writeMsbs.Add("m61_44_45_10");
					List<string> list54 = new List<string>
					{
						"EndIfEventFlag(EventEndType.End, OFF, TargetEventFlagType.EventFlag, 330)",
						"IfActionButtonInArea(MAIN, 9527, 2044451509)",
						"WaitFixedTimeSeconds(0.1)"
					};
					list54.AddRange(CS$<>8__locals1.<Write>g__warpToSide|38(side10));
					CS$<>8__locals1.<Write>g__addManualInit|49("m61_44_45_10", 0, list54);
				}
			}
			using (SortedDictionary<int, EventConfig.EvergaolEdit>.ValueCollection.Enumerator enumerator30 = CS$<>8__locals1.edits.EvergaolEdits.Values.GetEnumerator())
			{
				while (enumerator30.MoveNext())
				{
					EventConfig.EvergaolEdit gaol = enumerator30.Current;
					if (CS$<>8__locals1.opt["crawl"])
					{
						break;
					}
					AnnotationData.Area area8 = CS$<>8__locals1.defeatFlagAreas[gaol.DefeatFlag];
					Graph.Edge edge3 = CS$<>8__locals1.g.Nodes[area8.Name].From.FirstOrDefault<Graph.Edge>();
					if (edge3 != null)
					{
						string area9 = CS$<>8__locals1.g.EntranceIds[edge3.Name].Area;
						bool flag17 = area8.Name == "snowfield_evergaol";
						MSBE.Region.Other other5 = new MSBE.Region.Other();
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(15, 1);
						defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.Asset);
						defaultInterpolatedStringHandler.AppendLiteral(" evergaol range");
						other5.Name = defaultInterpolatedStringHandler.ToStringAndClear();
						other5.EntityID = num4++;
						MSBE.Region.Other other6 = other5;
						string text30;
						if (flag17)
						{
							text30 = "m60_12_14_02";
							MSBE.Part.Asset asset23 = CS$<>8__locals1.msbs[text30].Parts.Assets.Find((MSBE.Part.Asset o) => o.ModelName == "AEG110_009" && o.EntityGroupIDs.Contains((uint)gaol.Asset));
							if (asset23 == null)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(40, 2);
								defaultInterpolatedStringHandler.AppendLiteral("Evergaol asset AEG110_009 ");
								defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.Asset);
								defaultInterpolatedStringHandler.AppendLiteral(" not found in ");
								defaultInterpolatedStringHandler.AppendFormatted(text30);
								throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
							}
							other6.Position = asset23.Position + new Vector3(60f, -10f, 0f);
							other6.Shape = new MSB.Shape.Cylinder
							{
								Height = 50f,
								Radius = 120f
							};
						}
						else
						{
							text30 = area9;
							MSBE.Part.Asset asset23 = CS$<>8__locals1.msbs[text30].Parts.Assets.Find((MSBE.Part.Asset o) => (ulong)o.EntityID == (ulong)((long)gaol.Asset));
							if (asset23 == null)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(29, 2);
								defaultInterpolatedStringHandler.AppendLiteral("Evergaol asset ");
								defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.Asset);
								defaultInterpolatedStringHandler.AppendLiteral(" not found in ");
								defaultInterpolatedStringHandler.AppendFormatted(text30);
								throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
							}
							other6.Position = asset23.Position + new Vector3(0f, -5f, 0f);
							other6.Shape = new MSB.Shape.Cylinder
							{
								Height = 20f,
								Radius = 30f
							};
						}
						CS$<>8__locals1.msbs[text30].Regions.Add(other6);
						CS$<>8__locals1.writeMsbs.Add(text30);
						List<string> list55 = new List<string>();
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 1);
						defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.End, OFF, TargetEventFlagType.EventFlag, ");
						defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.ActiveFlag);
						defaultInterpolatedStringHandler.AppendLiteral(")");
						list55.Add(defaultInterpolatedStringHandler.ToStringAndClear());
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(65, 1);
						defaultInterpolatedStringHandler.AppendLiteral("IfAssetBackread(AND_01, ");
						defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.Asset);
						defaultInterpolatedStringHandler.AppendLiteral(", true, ComparisonType.GreaterOrEqual, 1)");
						list55.Add(defaultInterpolatedStringHandler.ToStringAndClear());
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(62, 1);
						defaultInterpolatedStringHandler.AppendLiteral("IfInoutsideArea(AND_01, InsideOutsideState.Inside, 10000, ");
						defaultInterpolatedStringHandler.AppendFormatted<uint>(other6.EntityID);
						defaultInterpolatedStringHandler.AppendLiteral(", 1)");
						list55.Add(defaultInterpolatedStringHandler.ToStringAndClear());
						list55.Add("EndIfConditionGroupStateUncompiled(EventEndType.End, PASS, AND_01)");
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
						defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
						defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.ActiveFlag);
						defaultInterpolatedStringHandler.AppendLiteral(", OFF)");
						list55.Add(defaultInterpolatedStringHandler.ToStringAndClear());
						List<string> cmds = list55;
						CS$<>8__locals1.<Write>g__addManualInit|49("common", 0, cmds);
						Graph.Edge edge4 = CS$<>8__locals1.g.Nodes[area8.Name].To.FirstOrDefault<Graph.Edge>();
						if (edge4 == null || edge4.Link == null)
						{
							if (!flag6)
							{
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(31, 1);
								defaultInterpolatedStringHandler.AppendLiteral("Evergaol defeat not connected: ");
								defaultInterpolatedStringHandler.AppendFormatted<Graph.Edge>(edge4);
								throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
							}
						}
						else
						{
							List<string> list56 = new List<string>();
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 1);
							defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.End, OFF, TargetEventFlagType.EventFlag, ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.DefeatFlag);
							defaultInterpolatedStringHandler.AppendLiteral(")");
							list56.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(65, 1);
							defaultInterpolatedStringHandler.AppendLiteral("IfAssetBackread(AND_01, ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.Asset);
							defaultInterpolatedStringHandler.AppendLiteral(", true, ComparisonType.GreaterOrEqual, 1)");
							list56.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(62, 1);
							defaultInterpolatedStringHandler.AppendLiteral("IfInoutsideArea(AND_01, InsideOutsideState.Inside, 10000, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(other6.EntityID);
							defaultInterpolatedStringHandler.AppendLiteral(", 1)");
							list56.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							list56.Add("EndIfConditionGroupStateUncompiled(EventEndType.End, FAIL, AND_01)");
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 1);
							defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.End, OFF, TargetEventFlagType.EventFlag, ");
							defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.ActiveFlag);
							defaultInterpolatedStringHandler.AppendLiteral(")");
							list56.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							List<string> list57 = list56;
							if (!flag17)
							{
								int value17 = CS$<>8__locals1.opt[Feature.RemoveRewards] ? 5 : 10;
								List<string> list58 = list57;
								string[] array30 = new string[13];
								array30[0] = "WaitFixedTimeFrames(1)";
								array30[1] = "SetSpEffect(10000, 190)";
								array30[2] = "ActivateGparamOverride(0, 0)";
								array30[3] = "ChangeWeather(Weather.PuffyClouds, -1, false)";
								int num84 = 4;
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(38, 1);
								defaultInterpolatedStringHandler.AppendLiteral("ChangeCharacterEnableState(");
								defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.NearbyGroup);
								defaultInterpolatedStringHandler.AppendLiteral(", Disabled)");
								array30[num84] = defaultInterpolatedStringHandler.ToStringAndClear();
								int num85 = 5;
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(41, 1);
								defaultInterpolatedStringHandler.AppendLiteral("ChangeCharacterCollisionState(");
								defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.NearbyGroup);
								defaultInterpolatedStringHandler.AppendLiteral(", Disabled)");
								array30[num85] = defaultInterpolatedStringHandler.ToStringAndClear();
								array30[6] = "SetSpEffect(10000, 514)";
								int num86 = 7;
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(33, 1);
								defaultInterpolatedStringHandler.AppendLiteral("ChangeAssetEnableState(");
								defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.Asset);
								defaultInterpolatedStringHandler.AppendLiteral(", Enabled)");
								array30[num86] = defaultInterpolatedStringHandler.ToStringAndClear();
								int num87 = 8;
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(38, 1);
								defaultInterpolatedStringHandler.AppendLiteral("CreateAssetfollowingSFX(");
								defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.Asset);
								defaultInterpolatedStringHandler.AppendLiteral(", 200, 806700)");
								array30[num87] = defaultInterpolatedStringHandler.ToStringAndClear();
								array30[9] = "ForceAnimationPlayback(10000, 60451, false, false, false, ComparisonType.Equal, 1)";
								array30[10] = "WaitFixedTimeSeconds(1)";
								array30[11] = "SetSpEffect(20000, 8870)";
								int num88 = 12;
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 1);
								defaultInterpolatedStringHandler.AppendLiteral("WaitFixedTimeSeconds(");
								defaultInterpolatedStringHandler.AppendFormatted<int>(value17);
								defaultInterpolatedStringHandler.AppendLiteral(")");
								array30[num88] = defaultInterpolatedStringHandler.ToStringAndClear();
								list58.AddRange(array30);
							}
							else
							{
								List<string> list59 = list57;
								string[] array31 = new string[7];
								array31[0] = "SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, 76652)";
								array31[1] = "WaitFixedTimeFrames(1)";
								int num89 = 2;
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
								defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
								defaultInterpolatedStringHandler.AppendFormatted<int>(gaol.ActiveFlag);
								defaultInterpolatedStringHandler.AppendLiteral(", OFF)");
								array31[num89] = defaultInterpolatedStringHandler.ToStringAndClear();
								array31[3] = "IfActionButtonInArea(MAIN, 9526, 1048571300)";
								array31[4] = "WaitFixedTimeFrames(1)";
								array31[5] = "ForceAnimationPlayback(10000, 60450, false, false, false, ComparisonType.Equal, 1)";
								array31[6] = "WaitFixedTimeSeconds(1)";
								list59.AddRange(array31);
							}
							list57.AddRange(CS$<>8__locals1.<Write>g__warpToSide|38(edge4.Link.Side));
							CS$<>8__locals1.<Write>g__addManualInit|49(area9, 0, list57);
						}
					}
				}
			}
			if (CS$<>8__locals1.opt[Feature.Segmented])
			{
				Graph.Edge edge5 = CS$<>8__locals1.g.Nodes["deeproot_dream"].From.FirstOrDefault<Graph.Edge>();
				if (edge5 != null && edge5.Link != null && !edge5.IsFixed)
				{
					AnnotationData.Area area10 = CS$<>8__locals1.g.Areas["deeproot_dream"];
					string area11 = CS$<>8__locals1.g.EntranceIds[edge5.Name].Area;
					int value18 = 12032880;
					int value19 = 12030858;
					int value20 = 12031850;
					List<string> list60 = new List<string>();
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 1);
					defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.End, OFF, TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(value19);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					list60.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(65, 1);
					defaultInterpolatedStringHandler.AppendLiteral("IfAssetBackread(AND_01, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(value20);
					defaultInterpolatedStringHandler.AppendLiteral(", true, ComparisonType.GreaterOrEqual, 1)");
					list60.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(62, 1);
					defaultInterpolatedStringHandler.AppendLiteral("IfInoutsideArea(AND_01, InsideOutsideState.Inside, 10000, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(value18);
					defaultInterpolatedStringHandler.AppendLiteral(", 1)");
					list60.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					list60.Add("EndIfConditionGroupStateUncompiled(EventEndType.End, PASS, AND_01)");
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
					defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(value19);
					defaultInterpolatedStringHandler.AppendLiteral(", OFF)");
					list60.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					List<string> cmds2 = list60;
					CS$<>8__locals1.<Write>g__addManualInit|49("common", 0, cmds2);
					Graph.Edge edge6 = CS$<>8__locals1.g.Nodes[area10.Name].To.FirstOrDefault<Graph.Edge>();
					if (edge6 == null || edge6.Link == null)
					{
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 1);
						defaultInterpolatedStringHandler.AppendLiteral("Fortissax defeat not connected: ");
						defaultInterpolatedStringHandler.AppendFormatted<Graph.Edge>(edge6);
						throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
					}
					List<string> list61 = new List<string>();
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 1);
					defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.End, OFF, TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(area10.DefeatFlag);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					list61.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(65, 1);
					defaultInterpolatedStringHandler.AppendLiteral("IfAssetBackread(AND_01, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(value20);
					defaultInterpolatedStringHandler.AppendLiteral(", true, ComparisonType.GreaterOrEqual, 1)");
					list61.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(62, 1);
					defaultInterpolatedStringHandler.AppendLiteral("IfInoutsideArea(AND_01, InsideOutsideState.Inside, 10000, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(value18);
					defaultInterpolatedStringHandler.AppendLiteral(", 1)");
					list61.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					list61.Add("EndIfConditionGroupStateUncompiled(EventEndType.End, FAIL, AND_01)");
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 1);
					defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.End, OFF, TargetEventFlagType.EventFlag, ");
					defaultInterpolatedStringHandler.AppendFormatted<int>(value19);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					list61.Add(defaultInterpolatedStringHandler.ToStringAndClear());
					List<string> list62 = list61;
					int value21 = 5;
					List<string> list63 = list62;
					string[] array32 = new string[8];
					array32[0] = "SetSpEffect(10000, 4280)";
					array32[1] = "SetSpEffect(10000, 4282)";
					array32[2] = "SetEventFlag(TargetEventFlagType.EventFlag, 12032870, ON)";
					array32[3] = "ChangeWeather(Weather.PuffyClouds, -1, true)";
					array32[4] = "WaitFixedTimeFrames(1)";
					array32[5] = "ChangeCharacterEnableState(12030950, Disabled)";
					array32[6] = "ChangeAssetEnableState(12031950, Disabled)";
					int num90 = 7;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 1);
					defaultInterpolatedStringHandler.AppendLiteral("WaitFixedTimeSeconds(");
					defaultInterpolatedStringHandler.AppendFormatted<int>(value21);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					array32[num90] = defaultInterpolatedStringHandler.ToStringAndClear();
					list63.AddRange(array32);
					list62.AddRange(CS$<>8__locals1.<Write>g__warpToSide|38(edge6.Link.Side));
					list62.AddRange(new string[]
					{
						"SetSpEffect(10000, 4281)",
						"SetSpEffect(10000, 4283)",
						"SetEventFlag(TargetEventFlagType.EventFlag, 12032870, OFF)"
					});
					CS$<>8__locals1.<Write>g__addManualInit|49(area11, 0, list62);
				}
			}
			int num91 = 141626;
			PARAM.Row row10 = GameEditor.AddRow(CS$<>8__locals1.Params["EquipParamGoods"], num91, 115);
			GameEditor.AddRow(CS$<>8__locals1.Params["SpEffectParam"], num91, 10600);
			row10["iconId"].Value = 244;
			row10["goodsUseAnim"].Value = 51;
			row10["castSfxId"].Value = 302170;
			row10["sortId"].Value = 501;
			row10["refId_default"].Value = num91;
			CS$<>8__locals1.itemFMGs["GoodsDialog"][num91] = "Return to the start without losing runes?";
			if (CS$<>8__locals1.g.Segments == null)
			{
				row10["yesNoDialogMessageId"].Value = num91;
				CS$<>8__locals1.itemFMGs["GoodsName"][num91] = "Bell of Return";
				CS$<>8__locals1.itemFMGs["GoodsInfo"][num91] = "Return to the start without losing runes";
				CS$<>8__locals1.itemFMGs["GoodsCaption"][num91] = "Item for use in Fog Gate Randomizer.\n\nReturn to the start without losing runes.\n\nIn the universe suddenly restored to its silence, the myriad\nwondering little voices of the earth rise up.";
				CS$<>8__locals1.<Write>g__addCommonInit|48("common_bellofreturn", 0, new List<object>
				{
					0
				});
			}
			else
			{
				row10["yesNoDialogMessageId"].Value = -1;
				row10["opmeMenuType"].Value = 0;
				CS$<>8__locals1.itemFMGs["GoodsName"][num91] = "Bell of Passage";
				CS$<>8__locals1.itemFMGs["GoodsInfo"][num91] = "Open a menu to warp to different segments";
				CS$<>8__locals1.itemFMGs["GoodsCaption"][num91] = "Item for use in Fog Gate Randomizer.\n\nOpen a menu to warp to different segments.";
				CS$<>8__locals1.<Write>g__addCommonInit|48("common_bellofpassage", 0, new List<object>
				{
					0
				});
				CS$<>8__locals1.<Write>g__addCommonInit|48("common_flagreturn", 0, new List<object>
				{
					0
				});
			}
			PARAM.Row row11 = GameEditor.AddRow(CS$<>8__locals1.Params["ItemLotParam_map"], (int)num7, 50000);
			row11["lotItemId01"].Value = num91;
			row11["getItemFlagId"].Value = num7;
			PARAM.Row row12 = GameEditor.AddRow(CS$<>8__locals1.Params["ItemLotParam_map"], 22000, 50000);
			row12["getItemFlagId"].Value = 0U;
			row12["lotItemId01"].Value = 22000;
			row12["lotItemNum01"].Value = 100;
			ESD esd = CS$<>8__locals1.esds["m00_00_00_00"]["t000001000"];
			List<long> list64 = ESDEdits.FindMachinesWithTalkData(esd, 15000390);
			if (list64.Count != 1)
			{
				throw new Exception("Can't edit grace menu: 'Memorize spell' message not found, or found multiple times (machines: [" + string.Join(", ", list64.Select(new Func<long, string>(AST.FormatMachine))) + "])");
			}
			Dictionary<long, ESD.State> dictionary24 = esd.StateGroups[list64[0]];
			CS$<>8__locals1.fmgIdBase = 478950000;
			int msg = CS$<>8__locals1.<Write>g__addMenuMsg|58("EventTextForTalk", CS$<>8__locals1.opt[Feature.Segmented] ? "Go to next" : "Repeat warp");
			if (list4.Count > 0)
			{
				List<AST.Expr> list65 = list4.Select(new Func<AnnotationData.Entrance, AST.Expr>(GameDataWriterE.<Write>g__getWarpCond|1_196)).ToList<AST.Expr>();
				ESDEdits.CustomTalkData customTalkData = new ESDEdits.CustomTalkData
				{
					LeaveMsg = 20000009,
					Msg = msg,
					ConsistentID = 70,
					Condition = AST.ChainExprs("||", list65),
					HighlightCondition = AST.Pass
				};
				long num92;
				ESDEdits.ModifyCustomTalkEntry(dictionary24, customTalkData, true, true, ref num92);
				ESD.State state5;
				if (!dictionary24.TryGetValue(num92, out state5))
				{
					throw new Exception("Could not add \"Repeat warp\" to grace menu");
				}
				list65.Add(AST.Pass);
				long value22 = state5.Conditions[0].TargetState.Value;
				state5.Conditions.Clear();
				long num93 = num92;
				List<ESD.State> list66 = AST.AllocateBranch(dictionary24, state5, list65, ref num93);
				for (int n2 = 0; n2 < list66.Count; n2++)
				{
					if (n2 < list4.Count)
					{
						Graph.WarpPoint warp6 = list4[n2].ASide.Warp;
						list66[n2].EntryCommands.Add(AST.MakeCommand(1, 11, new object[]
						{
							warp6.WarpFlag,
							1
						}));
						AST.Expr expr5 = new AST.BinaryExpr
						{
							Op = "#>",
							Lhs = AST.MakeFunction("f103", Array.Empty<object>()),
							Rhs = AST.MakeVal(1.5f)
						};
						list66[n2].Conditions.Add(new ESD.Condition(value22, AST.AssembleExpression(expr5)));
					}
					else
					{
						AST.CallState(list66[n2], value22);
					}
				}
			}
			ESD esd2 = CS$<>8__locals1.esds["m00_00_00_00"]["t000003000"];
			int leaveFlag = 11009260;
			foreach (Dictionary<long, ESD.State> dictionary25 in esd.StateGroups.Values.Concat(esd2.StateGroups.Values))
			{
				foreach (ESD.State state2 in dictionary25.Values)
				{
					foreach (ESD.Condition condition in state2.Conditions)
					{
						AST.Expr expr = AST.DisassembleExpression(condition.Evaluator);
						bool modified = false;
						expr = expr.Visit(AST.AstVisitor.Post(delegate(AST.Expr e)
						{
							if (GameDataWriterE.<Write>g__exprChecksFlags|1_62(e, leaveFlag))
							{
								modified = true;
								bool isLeyndellApproach = false;
								expr.Visit(AST.AstVisitor.PostAct(delegate(AST.Expr e2)
								{
									if (GameDataWriterE.<Write>g__exprChecksFlags|1_62(e2, 11002741))
									{
										isLeyndellApproach = true;
									}
								}), null);
								return GameDataWriterE.<Write>g__alwaysExpr|1_197(isLeyndellApproach, leaveFlag);
							}
							return null;
						}), null);
						if (modified)
						{
							condition.Evaluator = AST.AssembleExpression(expr);
						}
					}
				}
			}
			if (CS$<>8__locals1.opt["scadushop"] && CS$<>8__locals1.g.ExcludeMode == AnnotationData.AreaMode.None && !CS$<>8__locals1.opt[Feature.EldenCoin])
			{
				PARAM.Row row13 = GameEditor.AddRow(CS$<>8__locals1.Params["EquipParamGoods"], 22000, 10060);
				row13["maxNum"].Value = 10000;
				row13["maxRepositoryNum"].Value = 10000;
				row13["iconId"].Value = 248;
				CS$<>8__locals1.itemFMGs["GoodsName"][22000] = "Shadow Blessing Residue";
				CS$<>8__locals1.itemFMGs["GoodsInfo"][22000] = "Purchase materials in exchange for Shadow Realm Blessings";
				CS$<>8__locals1.itemFMGs["GoodsCaption"][22000] = "Currency for use in Fog Gate Randomizer.\r\n                    \r\nObtained by empowering Scadutree Blessing or Revered Spirit\r\nAsh Blessing when Shadow Realm Blessings are inactive.\r\n\r\nTrade for materials in the Shadow Realm Blessing menu at\r\nDLC Sites of Grace. More materials become available as you\r\naccumulate blessings.".Replace("\r\n", "\n");
				CS$<>8__locals1.menuFMGs["GR_Dialogues"][228000] = "Purchase <?itemName?>\nfor <?demandSoul?> blessing?";
				CS$<>8__locals1.menuFMGs["GR_LineHelp"][228000] = "Select item to purchase";
				CS$<>8__locals1.menuFMGs["GR_MenuText"][228000] = "Purchase Item";
				Dictionary<int, int> dictionary26 = new Dictionary<int, int>();
				dictionary26[10100] = 1;
				dictionary26[10101] = 1;
				dictionary26[10102] = 1;
				dictionary26[10103] = 1;
				dictionary26[10104] = 2;
				dictionary26[10105] = 2;
				dictionary26[10106] = 2;
				dictionary26[10107] = 2;
				dictionary26[10140] = 4;
				dictionary26[10160] = 3;
				dictionary26[10161] = 3;
				dictionary26[10162] = 3;
				dictionary26[10163] = 3;
				dictionary26[10164] = 4;
				dictionary26[10165] = 4;
				dictionary26[10166] = 5;
				dictionary26[10167] = 5;
				dictionary26[10200] = 5;
				dictionary26[10168] = 5;
				dictionary26[10010] = 2;
				dictionary26[10020] = 5;
				Dictionary<int, ValueTuple<int, int>> dictionary27 = new Dictionary<int, ValueTuple<int, int>>();
				dictionary27[10010] = new ValueTuple<int, int>(5, 0);
				dictionary27[10020] = new ValueTuple<int, int>(2, 1);
				Dictionary<int, ValueTuple<int, int>> dictionary28 = dictionary27;
				Dictionary<int, int> dictionary29 = new Dictionary<int, int>();
				dictionary29[10101] = 0;
				dictionary29[10102] = 1;
				dictionary29[10103] = 2;
				dictionary29[10104] = 3;
				dictionary29[10105] = 4;
				dictionary29[10106] = 5;
				dictionary29[10107] = 6;
				dictionary29[10140] = 7;
				dictionary29[10161] = 0;
				dictionary29[10162] = 1;
				dictionary29[10163] = 2;
				dictionary29[10164] = 3;
				dictionary29[10165] = 4;
				dictionary29[10166] = 5;
				dictionary29[10167] = 6;
				dictionary29[10200] = 7;
				dictionary29[10168] = 8;
				Dictionary<int, int> dictionary30 = dictionary29;
				CS$<>8__locals1.Params["ShopLineupParam"].Rows = (from r in CS$<>8__locals1.Params["ShopLineupParam"].Rows
				orderby r.ID
				select r).ToList<PARAM.Row>();
				int num94 = 727270000;
				int num95 = num94;
				foreach (KeyValuePair<int, int> keyValuePair12 in dictionary26)
				{
					int num96;
					keyValuePair12.Deconstruct(out i2, out num96);
					int num97 = i2;
					int num98 = num96;
					PARAM.Row row14 = GameEditor.AddRow(CS$<>8__locals1.Params["ShopLineupParam"], num95++, -1);
					row14["equipId"].Value = num97;
					row14["costType"].Value = 3;
					row14["value"].Value = num98;
					row14["equipType"].Value = 3;
					ValueTuple<int, int> valueTuple8;
					if (dictionary28.TryGetValue(num97, out valueTuple8))
					{
						ValueTuple<int, int> valueTuple9 = valueTuple8;
						int item19 = valueTuple9.Item1;
						int item20 = valueTuple9.Item2;
						row14["eventFlag_forStock"].Value = (long)((ulong)num12 + (ulong)((long)(item20 * 10)));
						row14["sellQuantity"].Value = (short)item19;
					}
					else
					{
						row14["sellQuantity"].Value = -1;
					}
					int num99;
					if (dictionary30.TryGetValue(num97, out num99))
					{
						row14["eventFlag_forRelease"].Value = (long)((ulong)num13 + (ulong)((long)num99));
					}
				}
				foreach (KeyValuePair<long, Dictionary<long, ESD.State>> keyValuePair13 in esd.StateGroups)
				{
					foreach (KeyValuePair<long, ESD.State> keyValuePair14 in keyValuePair13.Value)
					{
						ESD.State value23 = keyValuePair14.Value;
						if (value23.EntryCommands.Any((ESD.CommandCall c) => c.CommandBank == 1 && (c.CommandID == 152 || c.CommandID == 153)))
						{
							value23.EntryCommands.Add(AST.MakeCommand(1, 52, new object[]
							{
								3,
								22000,
								1
							}));
						}
					}
				}
				List<long> list67 = ESDEdits.FindMachinesWithCommand(esd, delegate(ESD.CommandCall c)
				{
					int num130;
					return c.CommandBank == 6 && c.Arguments.Count == 4 && AST.DisassembleExpression(c.Arguments[1]).TryAsInt(ref num130) && num130 == 20010002;
				});
				if (list67.Count != 1)
				{
					throw new Exception("Can't edit grace menu for shadow shop: menu not found: [" + string.Join(", ", list67.Select(new Func<long, string>(AST.FormatMachine))) + "]");
				}
				Dictionary<long, ESD.State> dictionary31 = esd.StateGroups[list67[0]];
				int msg2 = CS$<>8__locals1.<Write>g__addMenuMsg|58("EventTextForTalk", "Shadow Mart");
				ESDEdits.CustomTalkData customTalkData2 = new ESDEdits.CustomTalkData
				{
					LeaveMsg = 20010004,
					Msg = msg2,
					ConsistentID = 10,
					HighlightCondition = AST.MakeFunction("f47", new object[]
					{
						3,
						22000,
						2,
						0,
						0
					})
				};
				long key7;
				ESDEdits.ModifyCustomTalkEntry(dictionary31, customTalkData2, true, true, ref key7);
				ESD.State state3;
				if (!dictionary31.TryGetValue(key7, out state3))
				{
					throw new Exception("Could not add shadow shop to grace menu");
				}
				for (int num100 = 0; num100 < 10; num100++)
				{
					int num101 = (int)((ulong)num13 + (ulong)((long)num100));
					AST.Expr expr2 = AST.Binop(AST.Binop(AST.MakeFunction("f237", Array.Empty<object>()), "#>", num100), "||", AST.Binop(AST.MakeFunction("f238", Array.Empty<object>()), "#>", num100));
					state3.EntryCommands.Add(AST.MakeCommand(1, 11, new object[]
					{
						num101,
						expr2
					}));
				}
				state3.EntryCommands.Add(AST.MakeCommand(1, 145, new object[]
				{
					num94,
					num95
				}));
				AST.Expr expr3 = ESDEdits.MenuCloseExpr(29);
				state3.Conditions[0].Evaluator = AST.AssembleExpression(expr3);
			}
			foreach (PARAM.Row row15 in CS$<>8__locals1.Params["MapDefaultInfoParam"].Rows)
			{
				row15["EnableFastTravelEventFlagId"].Value = 0U;
			}
			foreach (MSBE.Part.Collision collision in CS$<>8__locals1.msbs["m34_12_00_00"].Parts.Collisions)
			{
				collision.EnableFastTravelEventFlagID = 0U;
			}
			HashSet<string> hashSet15 = new HashSet<string>();
			foreach (PARAM.Row row16 in CS$<>8__locals1.Params["BonfireWarpParam"].Rows)
			{
				if ((byte)row16["areaNo"].Value == 11 && (byte)row16["gridXNo"].Value == 0)
				{
					int num102 = (int)row16["textId1"].Value;
					string item21 = CS$<>8__locals1.itemFMGs["PlaceName"][num102];
					hashSet15.Add(item21);
				}
			}
			foreach (PARAM.Row row17 in CS$<>8__locals1.Params["BonfireWarpParam"].Rows)
			{
				if ((byte)row17["areaNo"].Value == 11 && (byte)row17["gridXNo"].Value == 5)
				{
					int num103 = (int)row17["textId1"].Value;
					string text31 = CS$<>8__locals1.itemFMGs["PlaceName"][num103];
					if (hashSet15.Contains(text31))
					{
						row17["posX"].Value = (float)row17["posX"].Value + 12f;
						row17["posZ"].Value = (float)row17["posZ"].Value - 12f;
						CS$<>8__locals1.itemFMGs["PlaceName"].Other[num103] = text31 + " (Ashen)";
					}
				}
			}
			MSBE.Part.Asset asset24 = CS$<>8__locals1.<Write>g__addFakeGate|25("m12_07_00_00", "AEG099_002", "AEG099_231_9000", new Vector3(601.429f, -497.836f, 974.806f), new Vector3(0f, 11.466f, 0f), null);
			asset24.EntityID = num4++;
			CS$<>8__locals1.<Write>g__addCommonFuncInit|47("disablecond", "m12_07_00_00", new List<object>
			{
				asset24.EntityID,
				310
			}, 0);
			CS$<>8__locals1.<Write>g__addFakeGate|25("m11_00_00_00", "AEG099_001", "AEG099_001_9006", new Vector3(-110.589f, 38f, -378.136f), new Vector3(0f, 88.332f, 0f), null);
			CS$<>8__locals1.<Write>g__addFakeGate|25("m11_05_00_00", "AEG099_001", "AEG099_001_9006", new Vector3(-110.799f, 38.015f, -378.361f), new Vector3(0f, -90.108f, 0f), null);
			CS$<>8__locals1.<Write>g__moveRegion|63("m10_00_00_00", 10002800U, new Vector3(-205.767f, 81.414f, 317.366f));
			CS$<>8__locals1.<Write>g__moveRegion|63("m15_00_00_00", 15002800U, new Vector3(41.699f, 52.618f, 517.615f));
			CS$<>8__locals1.<Write>g__moveRegion|63("m60_51_36_00", 1051362701U, new Vector3(77.8f, 90.76f, 37.87f));
			CS$<>8__locals1.msbs["m13_00_00_00"].Parts.Assets.RemoveAll((MSBE.Part.Asset o) => o.EntityID >= 13001570U && o.EntityID <= 13001573U);
			CS$<>8__locals1.msbs["m31_21_00_00"].Parts.Assets.RemoveAll((MSBE.Part.Asset o) => o.EntityID == 31211578U);
			MSBE.Event.ObjAct objAct7 = CS$<>8__locals1.msbs["m31_00_00_00"].Events.ObjActs.Find((MSBE.Event.ObjAct oa) => oa.EventFlagID == 31008522U);
			if (objAct7 != null)
			{
				objAct7.EventFlagID = 0U;
			}
			MSBE.Part.Enemy enemy9 = CS$<>8__locals1.msbs["m15_00_00_00"].Parts.Enemies.Find((MSBE.Part.Enemy e) => e.EntityID == 15000850U);
			if (enemy9 != null && enemy9.ModelName == "c4710")
			{
				enemy9.Position += new Vector3(0f, 0f, -3f);
			}
			if (ann.CustomBarriers != null)
			{
				foreach (AnnotationData.CustomBarrier customBarrier in ann.CustomBarriers)
				{
					if (!customBarrier.HasTag("inactive") && (CS$<>8__locals1.opt[Feature.SegmentFortresses] || customBarrier.HasTag("always")))
					{
						string[] assets = EventConfig.PhraseRe.Split(customBarrier.Assets);
						List<float> list68 = GameDataWriterE.<Write>g__parseFloats|1_20(customBarrier.Start.Split(' ', StringSplitOptions.None));
						List<float> list69 = GameDataWriterE.<Write>g__parseFloats|1_20(customBarrier.End.Split(' ', StringSplitOptions.None));
						float y = Math.Min(list68[1], list69[1]);
						Vector3 vector17 = new Vector3(list68[0], y, list68[2]);
						Vector3 vector18 = new Vector3(list69[0], y, list69[2]);
						float num104 = Vector3.Distance(vector17, vector18);
						Vector3 left2 = (vector18 - vector17) / num104;
						double num105 = Math.Atan2((double)(vector17.X - vector18.X), (double)(vector17.Z - vector18.Z));
						Vector3 rot2 = new Vector3(0f, (float)(num105 / 3.141592653589793 * 180.0) + 90f, 0f);
						bool[] array33 = new bool[2];
						array33[0] = true;
						foreach (bool flag18 in array33)
						{
							float num106 = (float)(flag18 ? 15 : 12);
							float num107 = (float)(flag18 ? 15 : 14);
							int num108 = (int)Math.Ceiling((double)(num104 / num106));
							for (int num109 = 0; num109 < num108; num109++)
							{
								Vector3 vector19 = vector17 + left2 * (num106 / 2f + (float)num109 * num106);
								MSBE.Part.Asset asset25 = CS$<>8__locals1.<Write>g__addFakeGate|25(customBarrier.Map, "AEG099_231", assets[0], vector19, rot2, null);
								if (flag18)
								{
									asset25.AssetSfxParamRelativeID = 0;
								}
								if (customBarrier.HasTag("double") || customBarrier.HasTag("triple"))
								{
									asset25 = CS$<>8__locals1.<Write>g__addFakeGate|25(customBarrier.Map, "AEG099_231", assets[0], vector19 + new Vector3(0f, num107, 0f), rot2, null);
									if (flag18)
									{
										asset25.AssetSfxParamRelativeID = 0;
									}
									if (customBarrier.HasTag("triple"))
									{
										asset25 = CS$<>8__locals1.<Write>g__addFakeGate|25(customBarrier.Map, "AEG099_231", assets[0], vector19 + new Vector3(0f, num107 * 2f, 0f), rot2, null);
										if (flag18)
										{
											asset25.AssetSfxParamRelativeID = 0;
										}
									}
								}
							}
						}
						if (!customBarrier.HasTag("keep"))
						{
							CS$<>8__locals1.msbs[customBarrier.Map].Parts.Assets.RemoveAll((MSBE.Part.Asset o) => assets.Contains(o.Name));
						}
					}
				}
				string text32 = "m60_36_50_00";
				HashSet<string> graveParts = new HashSet<string>
				{
					"AEG001_107_1007",
					"AEG001_107_1008",
					"AEG001_107_1009",
					"AEG001_107_1012",
					"AEG001_095_1007",
					"AEG001_095_1008",
					"AEG001_095_1010"
				};
				CS$<>8__locals1.msbs[text32].Parts.Assets.RemoveAll((MSBE.Part.Asset o) => graveParts.Contains(o.Name));
				CS$<>8__locals1.writeMsbs.Add(text32);
				string text33 = "m60_34_49_00";
				CS$<>8__locals1.emevds[text33].Events.RemoveAll((EMEVD.Event e) => e.ID == 1034492290L);
				CS$<>8__locals1.writeEmevds.Add(text33);
			}
			CS$<>8__locals1.menuFMGs["EventTextForMap"][666345] = "New open world Sites of Grace are available";
			CS$<>8__locals1.menuFMGs["EventTextForMap"][666346] = "You cannot proceed without defeating more powerful major bosses";
			CS$<>8__locals1.menuFMGs["EventTextForMap"][666347] = "The sealing tree cannot be burned while its guardian still lives.";
			if (CS$<>8__locals1.opt["crawl"] && ann.OpenBonfires != null)
			{
				List<string> list70 = (from a in CS$<>8__locals1.g.Areas.Values.Where(new Func<AnnotationData.Area, bool>(CS$<>8__locals1.g.IsMajorScalingBoss))
				select a.Name).ToList<string>();
				HashSet<uint> hashSet16 = new HashSet<uint>();
				for (int num110 = 0; num110 < CS$<>8__locals1.g.UnlockTiers.Count; num110++)
				{
					int num111 = CS$<>8__locals1.g.UnlockTiers[num110];
					if (num111 != -1)
					{
						List<string> list71 = new List<string>();
						uint value24 = num6 + (uint)num110;
						List<string> list72 = list71;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(69, 1);
						defaultInterpolatedStringHandler.AppendLiteral("EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, ");
						defaultInterpolatedStringHandler.AppendFormatted<uint>(value24);
						defaultInterpolatedStringHandler.AppendLiteral(")");
						list72.Add(defaultInterpolatedStringHandler.ToStringAndClear());
						foreach (string key8 in list70)
						{
							AnnotationData.Area area12 = CS$<>8__locals1.g.Areas[key8];
							int num112;
							if (CS$<>8__locals1.g.AreaTiers.TryGetValue(key8, out num112) && num112 >= num111 && area12.DefeatFlag > 0)
							{
								List<string> list73 = list71;
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(55, 1);
								defaultInterpolatedStringHandler.AppendLiteral("IfEventFlag(OR_01, ON, TargetEventFlagType.EventFlag, ");
								defaultInterpolatedStringHandler.AppendFormatted<int>(area12.DefeatFlag);
								defaultInterpolatedStringHandler.AppendLiteral(")");
								list73.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							}
						}
						if (list71.Count != 1)
						{
							list71.Add("IfConditionGroup(MAIN, PASS, OR_01)");
							List<string> list74 = list71;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(55, 1);
							defaultInterpolatedStringHandler.AppendLiteral("SkipIfEventFlag(2, ON, TargetEventFlagType.EventFlag, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(value);
							defaultInterpolatedStringHandler.AppendLiteral(")");
							list74.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							list71.Add("DisplayBlinkingMessage(666345)");
							List<string> list75 = list71;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 1);
							defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(value);
							defaultInterpolatedStringHandler.AppendLiteral(", ON)");
							list75.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							foreach (AnnotationData.OpenBonfire openBonfire in ann.OpenBonfires)
							{
								if (openBonfire.Tier > 0 && openBonfire.Tier <= num110 + 1 && (!openBonfire.HasTag("rauhruins") || !CS$<>8__locals1.opt["req_rauhruins"]))
								{
									List<string> list76 = list71;
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 1);
									defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
									defaultInterpolatedStringHandler.AppendFormatted<int>(openBonfire.Flag);
									defaultInterpolatedStringHandler.AppendLiteral(", ON)");
									list76.Add(defaultInterpolatedStringHandler.ToStringAndClear());
									hashSet16.Add((uint)openBonfire.Flag);
								}
							}
							List<string> list77 = list71;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 1);
							defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(value24);
							defaultInterpolatedStringHandler.AppendLiteral(", ON)");
							list77.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							list71.Add("WaitFixedTimeSeconds(0.1)");
							List<string> list78 = list71;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
							defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(value);
							defaultInterpolatedStringHandler.AppendLiteral(", OFF)");
							list78.Add(defaultInterpolatedStringHandler.ToStringAndClear());
							CS$<>8__locals1.<Write>g__addManualInit|49("common", 0, list71);
						}
					}
				}
				foreach (PARAM.Row row18 in CS$<>8__locals1.Params["BonfireWarpParam"].Rows)
				{
					uint item22 = (uint)row18["eventflagId"].Value;
					if (hashSet16.Contains(item22))
					{
						row18["iconId"].Value = 84;
						row18["forbiddenIconId"].Value = 84;
						row18["altIconId"].Value = 84;
						row18["altForbiddenIconId"].Value = 84;
					}
				}
				CS$<>8__locals1.Params["WorldMapPointParam"].Rows.RemoveAll((PARAM.Row row) => (ushort)row["iconId"].Value == 83);
			}
			if (CS$<>8__locals1.opt["physick"])
			{
				CS$<>8__locals1.<Write>g__addManualInit|49("common", 0, new string[]
				{
					"SetEventFlag(TargetEventFlagType.EventFlag, 11109774, ON)"
				});
			}
			foreach (Dictionary<long, ESD.State> dictionary32 in CS$<>8__locals1.esds["m11_05_00_00"]["t324001105"].StateGroups.Values)
			{
				if (dictionary32.Values.Any((ESD.State state) => state.EntryCommands.Any((ESD.CommandCall c) => GameDataWriterE.<Write>g__cmdSetFlags|1_60(c, 11053710))))
				{
					foreach (ESD.State state4 in dictionary32.Values)
					{
						foreach (ESD.Condition condition2 in state4.Conditions)
						{
							AST.Expr expr4 = AST.DisassembleExpression(condition2.Evaluator);
							bool modified = false;
							expr4 = expr4.Visit(AST.AstVisitor.Post(delegate(AST.Expr e)
							{
								AST.BinaryExpr binaryExpr = e as AST.BinaryExpr;
								if (binaryExpr != null)
								{
									AST.FunctionCall functionCall = binaryExpr.Lhs as AST.FunctionCall;
									if (functionCall != null && functionCall.Name == "f1")
									{
										modified = true;
										return new AST.BinaryExpr
										{
											Op = "&&",
											Lhs = e,
											Rhs = GameDataWriterE.<Write>g__eventFlag|1_57(11052855)
										};
									}
								}
								return null;
							}), null);
							if (modified)
							{
								condition2.Evaluator = AST.AssembleExpression(expr4);
							}
						}
					}
				}
			}
			if (CS$<>8__locals1.opt["crawl"])
			{
				ESDEdits.ForEachCondition(CS$<>8__locals1.esds["m61_00_00_00"]["t419006100"], delegate(ESD.Condition cond)
				{
					AST.Expr expr6 = AST.DisassembleExpression(cond.Evaluator);
					bool modified = false;
					expr6 = expr6.Visit(AST.AstVisitor.Post(delegate(AST.Expr e)
					{
						AST.FunctionCall functionCall = e as AST.FunctionCall;
						if (functionCall != null && functionCall.Name == "f211")
						{
							modified = true;
							return AST.MakeVal(2048430700);
						}
						return null;
					}), null);
					if (modified)
					{
						cond.Evaluator = AST.AssembleExpression(expr6);
					}
				});
				if (flag)
				{
					MSBE msbe3 = CS$<>8__locals1.msbs["m61_49_42_00"];
					msbe3.Regions.LockedMountJumps.Clear();
					msbe3.Regions.LockedMountJumpFalls.Clear();
					CS$<>8__locals1.writeMsbs.Add("m61_49_42_00");
				}
			}
			HashSet<string> hashSet17 = new HashSet<string>();
			using (List<AnnotationData.RetryPoint>.Enumerator enumerator41 = (ann.RetryPoints ?? new List<AnnotationData.RetryPoint>()).GetEnumerator())
			{
				while (enumerator41.MoveNext())
				{
					GameDataWriterE.<>c__DisplayClass1_44 CS$<>8__locals34 = new GameDataWriterE.<>c__DisplayClass1_44();
					CS$<>8__locals34.CS$<>8__locals6 = CS$<>8__locals1;
					CS$<>8__locals34.retry = enumerator41.Current;
					AnnotationData.Area area13;
					if (CS$<>8__locals34.retry.HasTag("remove") || (CS$<>8__locals34.CS$<>8__locals6.opt[Feature.SegmentFortresses] && CS$<>8__locals34.retry.HasTag("nofortress")))
					{
						MSBE.Event.RetryPoint retryPoint = CS$<>8__locals34.CS$<>8__locals6.msbs[CS$<>8__locals34.retry.Map].Events.RetryPoints.Find((MSBE.Event.RetryPoint ev) => ev.RetryPartName == CS$<>8__locals34.retry.Name);
						if (retryPoint != null)
						{
							CS$<>8__locals34.CS$<>8__locals6.msbs[CS$<>8__locals34.retry.Map].Events.RetryPoints.Remove(retryPoint);
							CS$<>8__locals34.CS$<>8__locals6.writeMsbs.Add(CS$<>8__locals34.retry.Map);
						}
					}
					else if (!string.IsNullOrEmpty(CS$<>8__locals34.retry.Area) && CS$<>8__locals34.CS$<>8__locals6.g.Areas.TryGetValue(CS$<>8__locals34.retry.Area, out area13) && CS$<>8__locals34.CS$<>8__locals6.<Write>g__shouldEditStake|69(area13))
					{
						GameDataWriterE.<>c__DisplayClass1_45 CS$<>8__locals35 = new GameDataWriterE.<>c__DisplayClass1_45();
						CS$<>8__locals35.CS$<>8__locals7 = CS$<>8__locals34;
						CS$<>8__locals35.ev = CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.msbs[CS$<>8__locals35.CS$<>8__locals7.retry.Map].Events.RetryPoints.Find((MSBE.Event.RetryPoint ev) => ev.RetryPartName == CS$<>8__locals35.CS$<>8__locals7.retry.Name);
						if (CS$<>8__locals35.ev != null)
						{
							if (area13.BossTrigger > 0)
							{
								CS$<>8__locals35.ev.EventFlagID = (uint)area13.BossTrigger;
							}
							hashSet17.Add(area13.Name);
							Graph.Edge edge7;
							Vector3 vector20;
							Vector3 rotation4;
							if (CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.<Write>g__getMainSpawnPoint|67(area13.Name, out edge7, out vector20, out rotation4))
							{
								CS$<>8__locals35.warp = edge7.Side.Warp;
								CS$<>8__locals35.stake = CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.msbs[CS$<>8__locals35.CS$<>8__locals7.retry.Map].Parts.Assets.Find((MSBE.Part.Asset o) => o.Name == CS$<>8__locals35.ev.RetryPartName);
								string text34 = CS$<>8__locals35.CS$<>8__locals7.retry.PlayerMap ?? CS$<>8__locals35.CS$<>8__locals7.retry.Map;
								MSBE.Part.Player player2 = CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.msbs[text34].Parts.Players.Find((MSBE.Part.Player p) => p.EntityID == CS$<>8__locals35.stake.EntityID - 970U);
								if (player2 == null)
								{
									Console.WriteLine("Stake for " + area13.Name + " doesn't have respawn point?");
								}
								else if (CS$<>8__locals35.warp.Map == text34)
								{
									player2.Position = vector20;
									player2.Rotation = rotation4;
									CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.writeMsbs.Add(text34);
								}
								else
								{
									MSBE.Event.RetryPoint retryPoint2 = CS$<>8__locals35.ev;
									if (CS$<>8__locals35.CS$<>8__locals7.retry.Map != CS$<>8__locals35.warp.Map)
									{
										retryPoint2 = (MSBE.Event.RetryPoint)CS$<>8__locals35.ev.DeepCopy();
										GameDataWriterE.<>c__DisplayClass1_46 CS$<>8__locals36;
										CS$<>8__locals36.copyStr = " " + CS$<>8__locals35.CS$<>8__locals7.retry.Area;
										MSBE.Event.RetryPoint retryPoint3 = retryPoint2;
										retryPoint3.Name += CS$<>8__locals36.copyStr;
										retryPoint2.MapID = -1;
										if (CS$<>8__locals35.ev.RetryRegionName != null)
										{
											GameDataWriterE.<>c__DisplayClass1_47 CS$<>8__locals37;
											CS$<>8__locals37.fromRegions = CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.msbs[CS$<>8__locals35.CS$<>8__locals7.retry.Map].Regions.GetEntries();
											CS$<>8__locals37.offset = CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.<Write>g__getMapOffset|28(CS$<>8__locals35.CS$<>8__locals7.retry.Map, CS$<>8__locals35.warp.Map);
											if (CS$<>8__locals35.CS$<>8__locals7.retry.Map == "m12_07_00_00")
											{
												CS$<>8__locals37.offset -= new Vector3(0f, 10f, 0f);
											}
											MSBE.Region region7 = CS$<>8__locals35.<Write>g__copyRegion|221(CS$<>8__locals35.ev.RetryRegionName, ref CS$<>8__locals36, ref CS$<>8__locals37);
											MSB.Shape.Composite composite = region7.Shape as MSB.Shape.Composite;
											if (composite != null)
											{
												foreach (MSB.Shape.Composite.Child child in composite.Children)
												{
													if (child.RegionName != null)
													{
														MSBE.Region region8 = CS$<>8__locals35.<Write>g__copyRegion|221(child.RegionName, ref CS$<>8__locals36, ref CS$<>8__locals37);
														child.RegionName = region8.Name;
													}
												}
											}
											retryPoint2.RetryRegionName = region7.Name;
										}
										MSBE.Part.Asset asset26 = CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.<Write>g__makeNewStake|68(edge7, vector20);
										asset26.EntityID = CS$<>8__locals35.stake.EntityID;
										CS$<>8__locals35.stake.EntityID = 0U;
										retryPoint2.RetryPartName = asset26.Name;
										CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.msbs[CS$<>8__locals35.CS$<>8__locals7.retry.Map].Events.RetryPoints.Remove(CS$<>8__locals35.ev);
										CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.msbs[CS$<>8__locals35.warp.Map].Events.RetryPoints.Add(retryPoint2);
										CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.writeMsbs.Add(CS$<>8__locals35.CS$<>8__locals7.retry.Map);
									}
									MSBE.Part.Player player3 = (MSBE.Part.Player)player2.DeepCopy();
									player3.Position = vector20;
									player3.Rotation = rotation4;
									player3.Name = CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.<Write>g__newPartName|12(CS$<>8__locals35.warp.Map, "c0000", player2.Name);
									player2.EntityID = 0U;
									CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.msbs[CS$<>8__locals35.warp.Map].Parts.Players.Add(player3);
									GameDataWriterE.<Write>g__addEnemyModel|1_22(CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.msbs[CS$<>8__locals35.warp.Map], player3.ModelName);
									CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.writeMsbs.Add(CS$<>8__locals35.warp.Map);
									CS$<>8__locals35.CS$<>8__locals7.CS$<>8__locals6.writeMsbs.Add(text34);
								}
							}
						}
					}
				}
			}
			uint num113 = 986021940U;
			Dictionary<string, uint> dictionary33 = new Dictionary<string, uint>();
			foreach (AnnotationData.Area area14 in CS$<>8__locals1.g.Areas.Values)
			{
				if (area14.OpenArea == null && area14.HasTag("sharedstake"))
				{
					dictionary33[area14.Name] = num10++;
				}
			}
			foreach (AnnotationData.Area area15 in CS$<>8__locals1.g.Areas.Values)
			{
				Graph.Edge edge8;
				Vector3 vector21;
				Vector3 rotation5;
				if (CS$<>8__locals1.<Write>g__shouldEditStake|69(area15) && !hashSet17.Contains(area15.Name) && CS$<>8__locals1.<Write>g__getMainSpawnPoint|67(area15.Name, out edge8, out vector21, out rotation5))
				{
					Graph.WarpPoint warp7 = edge8.Side.Warp;
					string text35 = (warp7 != null) ? warp7.Map : null;
					if (text35 == null)
					{
						throw new Exception("Internal error: stake for " + area15.Name + " missing destination map");
					}
					MSBE.Event.RetryPoint ev = new MSBE.Event.RetryPoint
					{
						MapID = -1,
						UnkS0C = -1,
						UnkE0C = byte.MaxValue,
						Name = area15.Name + " stake",
						EventFlagID = (uint)area15.BossTrigger
					};
					EMEVD emevd3;
					if (CS$<>8__locals1.emevds.TryGetValue(text35, out emevd3) && ev.EventFlagID > 0U && emevd3.Events.Any((EMEVD.Event emv) => emv.ID == (long)((ulong)ev.EventFlagID)))
					{
						uint num114 = num10++;
						CS$<>8__locals1.<Write>g__addCommonFuncInit|47("stakeflag", text35, new List<object>
						{
							area15.DefeatFlag,
							ev.EventFlagID,
							num114
						}, 0);
						ev.EventFlagID = num114;
					}
					if (ev.EventFlagID == 0U)
					{
						ev.EventFlagID = 6001U;
					}
					CS$<>8__locals1.msbs[text35].Events.RetryPoints.Add(ev);
					uint num115 = num113++;
					MSBE.Part.Asset asset27 = CS$<>8__locals1.<Write>g__makeNewStake|68(edge8, vector21);
					asset27.EntityID = num115;
					ev.RetryPartName = asset27.Name;
					MSBE.Part.Player player4 = new MSBE.Part.Player
					{
						ModelName = "c0000",
						EntityID = num115 - 970U
					};
					player4.Position = vector21;
					player4.Rotation = rotation5;
					player4.Name = CS$<>8__locals1.<Write>g__newPartName|12(text35, player4.ModelName, asset27.Name);
					CS$<>8__locals1.msbs[text35].Parts.Players.Add(player4);
					GameDataWriterE.<Write>g__addEnemyModel|1_22(CS$<>8__locals1.msbs[text35], player4.ModelName);
					if (edge8.Side.StakeRegions != null)
					{
						List<uint> regionIds = edge8.Side.StakeRegions.Split(' ', StringSplitOptions.None).Select(new Func<string, uint>(uint.Parse)).ToList<uint>();
						List<MSBE.Region> list79 = (from r in CS$<>8__locals1.msbs[text35].Regions.GetEntries()
						where regionIds.Contains(r.EntityID)
						select r).ToList<MSBE.Region>();
						MSBE.Region region9 = new MSBE.Region.Other
						{
							Name = player4.Name + " region"
						};
						MSB.Shape.Composite composite2 = new MSB.Shape.Composite();
						for (int num116 = 0; num116 < list79.Count; num116++)
						{
							composite2.Children[num116].RegionName = list79[num116].Name;
						}
						if (list79.Count > 0)
						{
							region9.Shape = composite2;
							CS$<>8__locals1.msbs[text35].Regions.Add(region9);
							ev.RetryRegionName = region9.Name;
						}
						else
						{
							ev.EventFlagID = 6000U;
						}
					}
					else
					{
						int num117 = (area15.StakeRadius > 0) ? area15.StakeRadius : 150;
						if (area15.BossTrigger > 0)
						{
							ev.UnkT08 = (float)num117;
							MSBE.Region.Other other7 = new MSBE.Region.Other
							{
								Name = player4.Name + " range",
								Shape = new MSB.Shape.Cylinder
								{
									Radius = (float)num117,
									Height = 5f
								},
								Position = asset27.Position,
								Rotation = asset27.Rotation
							};
							CS$<>8__locals1.msbs[text35].Regions.Add(other7);
						}
						else
						{
							MSBE.Region.Other other8 = new MSBE.Region.Other
							{
								Name = player4.Name + " stake",
								Shape = new MSB.Shape.Cylinder
								{
									Radius = (float)num117,
									Height = 100f
								},
								Position = asset27.Position - new Vector3(0f, 50f, 0f),
								Rotation = asset27.Rotation
							};
							CS$<>8__locals1.msbs[text35].Regions.Add(other8);
							ev.RetryRegionName = other8.Name;
						}
					}
					CS$<>8__locals1.writeMsbs.Add(text35);
				}
			}
			if ((CS$<>8__locals1.opt["newgraces"] || CS$<>8__locals1.opt[Feature.ChapelInit]) && ann.CustomBonfires != null)
			{
				int num118 = 986030;
				HashSet<uint> hashSet18 = new HashSet<uint>(from r in CS$<>8__locals1.Params["BonfireWarpParam"].Rows
				select (uint)r["eventflagId"].Value);
				GameDataWriterE.<>c__DisplayClass1_51 CS$<>8__locals40;
				CS$<>8__locals40.bonfireEntities = new HashSet<uint>(from r in CS$<>8__locals1.Params["BonfireWarpParam"].Rows
				select (uint)r["bonfireEntityId"].Value);
				HashSet<int> hashSet19 = new HashSet<int>(from r in CS$<>8__locals1.Params["BonfireWarpParam"].Rows
				select r.ID);
				Dictionary<int, List<int>> dictionary34 = new Dictionary<int, List<int>>();
				using (List<AnnotationData.CustomBonfire>.Enumerator enumerator43 = ann.CustomBonfires.GetEnumerator())
				{
					while (enumerator43.MoveNext())
					{
						GameDataWriterE.<>c__DisplayClass1_52 CS$<>8__locals41 = new GameDataWriterE.<>c__DisplayClass1_52();
						CS$<>8__locals41.b = enumerator43.Current;
						if (!CS$<>8__locals41.b.HasTag("inactive") && (!CS$<>8__locals41.b.HasTag("chapel") || CS$<>8__locals1.opt[Feature.ChapelInit]) && (CS$<>8__locals41.b.HasTag("chapel") || CS$<>8__locals1.opt["newgraces"]))
						{
							PARAM.Row row19 = CS$<>8__locals1.Params["BonfireWarpParam"].Rows.Find((PARAM.Row r) => (ulong)((uint)r["bonfireEntityId"].Value) == (ulong)((long)CS$<>8__locals41.b.Base));
							int baseChrId = CS$<>8__locals41.b.Base - 1000;
							int basePlayerId = CS$<>8__locals41.b.Base - 970;
							MSBE.Part.Enemy enemy10 = CS$<>8__locals1.msbs[CS$<>8__locals41.b.Map].Parts.Enemies.Find(delegate(MSBE.Part.Enemy e)
							{
								if (CS$<>8__locals41.b.Enemy != null)
								{
									return e.Name == CS$<>8__locals41.b.Enemy;
								}
								return (ulong)e.EntityID == (ulong)((long)baseChrId);
							});
							MSBE.Part.Player player5 = CS$<>8__locals1.msbs[CS$<>8__locals41.b.Map].Parts.Players.Find(delegate(MSBE.Part.Player e)
							{
								if (CS$<>8__locals41.b.Player != null)
								{
									return e.Name == CS$<>8__locals41.b.Player;
								}
								return (ulong)e.EntityID == (ulong)((long)basePlayerId);
							});
							MSBE.Part.Asset asset28 = CS$<>8__locals1.msbs[CS$<>8__locals41.b.Map].Parts.Assets.Find((MSBE.Part.Asset e) => e.Name == CS$<>8__locals41.b.Asset);
							if (row19 != null && enemy10 != null && player5 != null && asset28 != null)
							{
								GameDataWriterE.<Write>g__addBonfireEntities|1_233(CS$<>8__locals1.msbs[CS$<>8__locals41.b.Map].Parts.Enemies, 1000U, ref CS$<>8__locals40);
								GameDataWriterE.<Write>g__addBonfireEntities|1_233(CS$<>8__locals1.msbs[CS$<>8__locals41.b.Map].Parts.Players, 970U, ref CS$<>8__locals40);
								uint num119 = (uint)row19["eventflagId"].Value;
								List<float> list80 = GameDataWriterE.<Write>g__parseFloats|1_20(CS$<>8__locals41.b.Location.Split(' ', StringSplitOptions.None));
								Vector3 vector22 = new Vector3(list80[0], list80[1], list80[2]);
								Vector3 vector23 = new Vector3(0f, list80[3], 0f);
								Vector3 position5 = GameDataWriterE.<Write>g__moveInDirection|1_29(vector22, vector23, 2f);
								uint num120 = (uint)CS$<>8__locals41.b.Base;
								if (CS$<>8__locals41.b.HasTag("chapel"))
								{
									num120 = 10011952U;
								}
								while (CS$<>8__locals40.bonfireEntities.Contains(num120))
								{
									num120 += 1U;
								}
								CS$<>8__locals40.bonfireEntities.Add(num120);
								uint entityID5 = num120 - 1000U;
								uint entityID6 = num120 - 970U;
								CS$<>8__locals1.<Write>g__addFakeGate|25(CS$<>8__locals41.b.Map, "AEG099_060", CS$<>8__locals41.b.Asset, vector22, vector23, null).EntityID = num120;
								MSBE.Part.Enemy enemy11 = (MSBE.Part.Enemy)enemy10.DeepCopy();
								enemy11.EntityID = entityID5;
								if (enemy11.ModelName != "c1000")
								{
									enemy11.ModelName = "c1000";
									enemy11.ThinkParamID = 1;
									enemy11.NPCParamID = 10000000;
									enemy11.TalkID = 1000;
									enemy11.CharaInitID = -1;
									if (CS$<>8__locals41.b.HasTag("chapel"))
									{
										enemy11.CollisionPartName = "h002000";
									}
								}
								enemy11.Name = CS$<>8__locals1.<Write>g__newPartName|12(CS$<>8__locals41.b.Map, "c1000", enemy10.Name);
								GameDataWriterE.<Write>g__setNameIdent|1_23(enemy11);
								enemy11.Position = vector22;
								enemy11.Rotation = vector23;
								CS$<>8__locals1.msbs[CS$<>8__locals41.b.Map].Parts.Enemies.Add(enemy11);
								MSBE.Part.Player player6 = (MSBE.Part.Player)player5.DeepCopy();
								player6.EntityID = entityID6;
								player6.Name = CS$<>8__locals1.<Write>g__newPartName|12(CS$<>8__locals41.b.Map, "c0000", player5.Name);
								GameDataWriterE.<Write>g__setNameIdent|1_23(player6);
								player6.Position = position5;
								player6.Rotation = vector23;
								CS$<>8__locals1.msbs[CS$<>8__locals41.b.Map].Parts.Players.Add(player6);
								CS$<>8__locals1.writeMsbs.Add(CS$<>8__locals41.b.Map);
								int num121 = row19.ID;
								if (CS$<>8__locals41.b.HasTag("chapel"))
								{
									num121 = 100102;
								}
								uint num122 = num119;
								while (hashSet19.Contains(num121))
								{
									num121++;
								}
								while (hashSet18.Contains(num122))
								{
									num122 += 1U;
								}
								hashSet19.Add(num121);
								hashSet18.Add(num122);
								int num123 = num118++;
								CS$<>8__locals1.itemFMGs["PlaceName"][num123] = CS$<>8__locals41.b.Text;
								PARAM.Row row20 = GameEditor.AddRow(CS$<>8__locals1.Params["BonfireWarpParam"], num121, -1);
								row20["eventflagId"].Value = num122;
								row20["bonfireEntityId"].Value = num120;
								int num124 = (int)((ushort)row19["bonfireSubCategorySortId"].Value);
								Util.AddMulti<int, int>(dictionary34, row19.ID, num121);
								row20["bonfireSubCategorySortId"].Value = (ushort)(num124 + dictionary34[row19.ID].Count);
								byte[] array34 = GameDataWriterE.<Write>g__parseMap|1_32(CS$<>8__locals41.b.Map);
								row20["areaNo"].Value = array34[0];
								row20["gridXNo"].Value = array34[1];
								row20["gridZNo"].Value = array34[2];
								row20["posX"].Value = vector22.X;
								row20["posY"].Value = vector22.Y;
								row20["posZ"].Value = vector22.Z;
								row20["textId1"].Value = num123;
								foreach (string text36 in new List<string>
								{
									"forbiddenIconId",
									"bonfireSubCategoryId",
									"iconId",
									"dispMask00",
									"dispMask01",
									"dispMask02",
									"noIgnitionSfxDmypolyId_0",
									"noIgnitionSfxId_0"
								})
								{
									row20[text36].Value = row19[text36].Value;
								}
								Events events3 = CS$<>8__locals1.events;
								defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(31, 2);
								defaultInterpolatedStringHandler.AppendLiteral("RegisterBonfire(");
								defaultInterpolatedStringHandler.AppendFormatted<uint>(num9++);
								defaultInterpolatedStringHandler.AppendLiteral(", ");
								defaultInterpolatedStringHandler.AppendFormatted<uint>(num120);
								defaultInterpolatedStringHandler.AppendLiteral(", 0, 0, 0, 5)");
								EMEVD.Instruction init = events3.ParseAdd(defaultInterpolatedStringHandler.ToStringAndClear());
								CS$<>8__locals1.<Write>g__addInit|46(CS$<>8__locals41.b.Map, init, 0);
							}
						}
					}
				}
				CS$<>8__locals1.Params["BonfireWarpParam"].Rows = (from r in CS$<>8__locals1.Params["BonfireWarpParam"].Rows
				orderby r.ID
				select r).ToList<PARAM.Row>();
			}
			GameDataWriterE.<Write>g__cloneWhere|1_71(CS$<>8__locals1.msbs["m11_00_00_00"], (MSBE.Part.ConnectCollision con) => con.MapID[0] == 35);
			GameDataWriterE.<Write>g__cloneWhere|1_71(CS$<>8__locals1.msbs["m11_05_00_00"], (MSBE.Part.ConnectCollision con) => con.MapID[0] == 35);
			GameDataWriterE.<Write>g__cloneWhere|1_71(CS$<>8__locals1.msbs["m35_00_00_00"], (MSBE.Part.ConnectCollision con) => con.MapID[0] == 11);
			GameDataWriterE.<Write>g__cloneWhere|1_71(CS$<>8__locals1.msbs["m20_00_00_00"], (MSBE.Part.ConnectCollision con) => con.MapID[0] == 20);
			if (CS$<>8__locals1.opt[Feature.Segmented])
			{
				GameDataWriterE.<Write>g__cloneWhere|1_71(CS$<>8__locals1.msbs["m11_05_00_00"], (MSBE.Part.ConnectCollision con) => con.MapID[0] == 19);
				GameDataWriterE.<Write>g__cloneWhere|1_71(CS$<>8__locals1.msbs["m19_00_00_00"], (MSBE.Part.ConnectCollision con) => con.MapID[0] == 11);
			}
			int num125 = 755850300;
			SortedDictionary<int, int> sortedDictionary2 = new SortedDictionary<int, int>();
			sortedDictionary2[8600] = 62010;
			sortedDictionary2[8601] = 62011;
			sortedDictionary2[8602] = 62012;
			sortedDictionary2[8603] = 62020;
			sortedDictionary2[8604] = 62021;
			sortedDictionary2[8605] = 62022;
			sortedDictionary2[8606] = 62030;
			sortedDictionary2[8607] = 62031;
			sortedDictionary2[8608] = 62032;
			sortedDictionary2[8609] = 62040;
			sortedDictionary2[8610] = 62041;
			sortedDictionary2[8611] = 62050;
			sortedDictionary2[8612] = 62051;
			sortedDictionary2[8613] = 62060;
			sortedDictionary2[8614] = 62061;
			sortedDictionary2[8615] = 62063;
			sortedDictionary2[8616] = 62062;
			sortedDictionary2[8617] = 62064;
			sortedDictionary2[8618] = 62052;
			sortedDictionary2[2008600] = 62080;
			sortedDictionary2[2008601] = 62081;
			sortedDictionary2[2008602] = 62082;
			sortedDictionary2[2008603] = 62083;
			sortedDictionary2[2008604] = 62084;
			List<EMEVD.Instruction> list81 = new List<EMEVD.Instruction>();
			foreach (KeyValuePair<int, int> keyValuePair15 in sortedDictionary2)
			{
				int value25 = keyValuePair15.Value;
				list81.Add(new EMEVD.Instruction(2003, 66, new List<object>
				{
					0,
					value25,
					1
				}));
			}
			list81.Add(new EMEVD.Instruction(2003, 66, new List<object>
			{
				0,
				82001,
				1
			}));
			list81.Add(new EMEVD.Instruction(2003, 66, new List<object>
			{
				0,
				62002,
				1
			}));
			list81.Add(new EMEVD.Instruction(2003, 66, new List<object>
			{
				0,
				82002,
				1
			}));
			Events.AddSimpleEvent(CS$<>8__locals1.emevds["common"], num125++, list81, 0);
			EMEVD.Event event5 = CS$<>8__locals1.emevds["common"].Events.Find((EMEVD.Event e) => e.ID == 1600L);
			if (event5 != null)
			{
				Events.OldParams oldParams = Events.OldParams.Preprocess(event5);
				event5.Instructions.RemoveAll((EMEVD.Instruction ins) => (ins.Bank == 2003 && ins.ID == 66) || (ins.Bank == 2007 && ins.ID == 2));
				oldParams.Postprocess();
			}
			CS$<>8__locals1.menuFMGs["EventTextForMap"][666401] = "Error: Unrestricted item placement was enabled in Item Randomizer,\nbut Fog Gate Randomizer was not detected";
			CS$<>8__locals1.emevds["common"].Events.RemoveAll((EMEVD.Event ev) => ev.ID == 19003112L);
			if (CS$<>8__locals1.opt["cheat"])
			{
				if (CS$<>8__locals1.opt["telescope"])
				{
					Events.AddSimpleEvent(CS$<>8__locals1.emevds["common"], num125++, new List<string>
					{
						"IfCharacterHasSpEffect(MAIN, 10000, 3240, true, ComparisonType.Equal, 1)",
						"ForceCharacterDeath(10000, false)"
					}.Select(new Func<string, EMEVD.Instruction>(CS$<>8__locals1.events.ParseAdd)), 0);
				}
				if (CS$<>8__locals1.opt["bonfire"])
				{
					List<EMEVD.Instruction> list82 = new List<EMEVD.Instruction>();
					(from r in CS$<>8__locals1.Params["WorldMapPointParam"].Rows
					select (ushort)r["iconId"].Value).ToList<ushort>();
					foreach (PARAM.Row row21 in CS$<>8__locals1.Params["BonfireWarpParam"].Rows)
					{
						uint num126 = (uint)row21["eventflagId"].Value;
						if (num126 / 10000U == 7U && num126 != 73450U)
						{
							List<EMEVD.Instruction> list83 = list82;
							Events events4 = CS$<>8__locals1.events;
							defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 1);
							defaultInterpolatedStringHandler.AppendLiteral("SetEventFlag(TargetEventFlagType.EventFlag, ");
							defaultInterpolatedStringHandler.AppendFormatted<uint>(num126);
							defaultInterpolatedStringHandler.AppendLiteral(", ON)");
							list83.Add(events4.ParseAdd(defaultInterpolatedStringHandler.ToStringAndClear()));
						}
					}
					Events.AddSimpleEvent(CS$<>8__locals1.emevds["common"], num125++, list82, 0);
				}
				if (CS$<>8__locals1.opt["cheatkeys"])
				{
					List<int> list84 = new List<int>();
					list84.Add(8105);
					list84.Add(8106);
					list84.Add(8107);
					list84.Add(8109);
					list84.Add(8111);
					list84.Add(8175);
					list84.Add(8176);
					List<EMEVD.Instruction> list85 = new List<EMEVD.Instruction>();
					int num127 = 1;
					foreach (int value26 in list84)
					{
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(4, 1);
						defaultInterpolatedStringHandler.AppendLiteral("AND_");
						defaultInterpolatedStringHandler.AppendFormatted<int>(num127++, "d2");
						string text37 = defaultInterpolatedStringHandler.ToStringAndClear();
						List<EMEVD.Instruction> list86 = list85;
						Events events5 = CS$<>8__locals1.events;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 2);
						defaultInterpolatedStringHandler.AppendLiteral("IfPlayerHasdoesntHaveItem(");
						defaultInterpolatedStringHandler.AppendFormatted(text37);
						defaultInterpolatedStringHandler.AppendLiteral(", ItemType.Goods, ");
						defaultInterpolatedStringHandler.AppendFormatted<int>(value26);
						defaultInterpolatedStringHandler.AppendLiteral(", OwnershipState.Owns)");
						list86.Add(events5.ParseAdd(defaultInterpolatedStringHandler.ToStringAndClear()));
						list85.Add(CS$<>8__locals1.events.ParseAdd("SkipIfConditionGroupStateUncompiled(1, PASS, " + text37 + ")"));
						List<EMEVD.Instruction> list87 = list85;
						Events events6 = CS$<>8__locals1.events;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 1);
						defaultInterpolatedStringHandler.AppendLiteral("DirectlyGivePlayerItem(ItemType.Goods, ");
						defaultInterpolatedStringHandler.AppendFormatted<int>(value26);
						defaultInterpolatedStringHandler.AppendLiteral(", 6001, 1)");
						list87.Add(events6.ParseAdd(defaultInterpolatedStringHandler.ToStringAndClear()));
					}
					Events.AddSimpleEvent(CS$<>8__locals1.emevds["common"], num125++, list85, 0);
				}
			}
			if (CS$<>8__locals1.opt["dryrun"])
			{
				return;
			}
			if (notify != null)
			{
				notify("Writing game data");
			}
			CS$<>8__locals1.overrideDcx = 9;
			Console.WriteLine("Writing params");
			string text38 = CS$<>8__locals1.outDir + "\\regulation.bin";
			DCX.Type type = 13;
			CS$<>8__locals1.editor.OverrideBndRel<PARAM>(text5, text38, CS$<>8__locals1.Params.Inner, delegate(PARAM f)
			{
				if (f.AppliedParamdef != null)
				{
					return f.Write();
				}
				return null;
			}, null, type);
			CS$<>8__locals1.dupedPaths = new HashSet<string>();
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(15, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Writing ");
			defaultInterpolatedStringHandler.AppendFormatted<int>(CS$<>8__locals1.writeEmevds.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" emevds");
			Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
			using (Dictionary<string, EMEVD>.Enumerator enumerator12 = CS$<>8__locals1.emevds.GetEnumerator())
			{
				while (enumerator12.MoveNext())
				{
					KeyValuePair<string, EMEVD> entry = enumerator12.Current;
					foreach (EMEVD.Event event6 in entry.Value.Events)
					{
						int num96;
						int i;
						for (i = 0; i < event6.Instructions.Count; i = num96 + 1)
						{
							EMEVD.Instruction instruction5 = event6.Instructions[i];
							if (instruction5.Bank == 2003 && instruction5.ID == 28)
							{
								event6.Instructions[i] = new EMEVD.Instruction(1014, 69);
								event6.Parameters.RemoveAll((EMEVD.Parameter p) => p.InstructionIndex == (long)i);
							}
							else if (CS$<>8__locals1.opt["nohit"] && instruction5.Bank == 2007 && instruction5.ID == 1)
							{
								Events.Instr instr = CS$<>8__locals1.events.Parse(instruction5, null);
								if (instr[1].ToString() == "1" && instr[2].ToString() == "6")
								{
									EMEVD.Instruction value27 = new EMEVD.Instruction(2007, 4, new List<object>
									{
										instr[0]
									});
									event6.Instructions[i] = value27;
									event6.Parameters.RemoveAll((EMEVD.Parameter p) => p.InstructionIndex == (long)i);
								}
							}
							num96 = i;
						}
					}
					string path3 = CS$<>8__locals1.outDir + "\\event\\" + entry.Key + ".emevd.dcx";
					CS$<>8__locals1.<Write>g__writeWithDupe|80(CS$<>8__locals1.writeEmevds, entry.Key, path3, delegate(string p)
					{
						entry.Value.Write(p, CS$<>8__locals1.overrideDcx);
					});
				}
			}
			Console.WriteLine("Writing FMGs");
			string text39 = CS$<>8__locals1.outDir + "\\msg\\engus\\item_dlc02.msgbnd.dcx";
			CS$<>8__locals1.editor.OverrideBndRel<FMG>(text3, text39, CS$<>8__locals1.itemFMGs.FMGs, (FMG f) => f.Write(), null, CS$<>8__locals1.overrideDcx);
			string text40 = CS$<>8__locals1.outDir + "\\msg\\engus\\menu_dlc02.msgbnd.dcx";
			CS$<>8__locals1.editor.OverrideBndRel<FMG>(text4, text40, CS$<>8__locals1.menuFMGs.FMGs, (FMG f) => f.Write(), null, CS$<>8__locals1.overrideDcx);
			Console.WriteLine("Writing ESDs");
			if (CS$<>8__locals1.copyEsdsFrom.Count > 0)
			{
				List<string> list88 = (from x in CS$<>8__locals1.copyEsdsFrom.SelectMany((KeyValuePair<ValueTuple<string, int>, ValueTuple<string, int>> e) => new string[]
				{
					e.Key.Item1,
					e.Value.Item1
				}).Concat(CS$<>8__locals1.esds.Keys)
				orderby x
				select x).Distinct<string>().ToList<string>();
				HashSet<string> hashSet20 = new HashSet<string>
				{
					"m39_20_00_00"
				};
				Dictionary<string, BND4> dictionary35 = new Dictionary<string, BND4>();
				foreach (string text41 in list88)
				{
					string text42 = CS$<>8__locals1.<Write>g__resolvePath|0(CS$<>8__locals1.editor.Spec.GameDir + "\\" + text41 + ".talkesdbnd.dcx", "script\\talk", false);
					if (File.Exists(text42))
					{
						dictionary35[text41] = SoulsFile<BND4>.Read(text42);
					}
					else
					{
						if (!hashSet20.Contains(text41))
						{
							throw new Exception(text42 + " not found but was expected to exist");
						}
						BND4 bnd2 = SoulsFile<BND4>.Read(CS$<>8__locals1.editor.Spec.GameDir + "\\m00_00_00_00.talkesdbnd.dcx");
						bnd2.Files.Clear();
						dictionary35[text41] = bnd2;
					}
				}
				using (List<string>.Enumerator enumerator9 = list88.GetEnumerator())
				{
					while (enumerator9.MoveNext())
					{
						string esdMap = enumerator9.Current;
						BND4 bnd3 = dictionary35[esdMap];
						bool flag19 = CS$<>8__locals1.copyEsdsFrom.Any((KeyValuePair<ValueTuple<string, int>, ValueTuple<string, int>> e) => e.Key.Item1 == esdMap);
						if (flag19 || CS$<>8__locals1.esds.ContainsKey(esdMap))
						{
							if (flag19)
							{
								int num128;
								if (bnd3.Files.Count != 0)
								{
									num128 = bnd3.Files.MaxBy((BinderFile f) => f.ID).ID + 1;
								}
								else
								{
									num128 = 0;
								}
								int num129 = num128;
								foreach (KeyValuePair<ValueTuple<string, int>, ValueTuple<string, int>> keyValuePair16 in CS$<>8__locals1.copyEsdsFrom)
								{
									ValueTuple<string, int> valueTuple10;
									ValueTuple<string, int> valueTuple11;
									keyValuePair16.Deconstruct(out valueTuple10, out valueTuple11);
									ValueTuple<string, int> valueTuple12 = valueTuple10;
									ValueTuple<string, int> valueTuple13 = valueTuple11;
									string item23 = valueTuple12.Item1;
									int item24 = valueTuple12.Item2;
									string item25 = valueTuple13.Item1;
									int item26 = valueTuple13.Item2;
									if (!(item23 != esdMap))
									{
										string sourceName = GameDataWriterE.<Write>g__tId|1_246(item26);
										string text43 = GameDataWriterE.<Write>g__tId|1_246(item24);
										BinderFile binderFile2 = dictionary35[item25].Files.Find((BinderFile f) => f.Name.EndsWith(sourceName));
										if (binderFile2 == null)
										{
											defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(38, 4);
											defaultInterpolatedStringHandler.AppendFormatted(sourceName);
											defaultInterpolatedStringHandler.AppendLiteral(" not found in ");
											defaultInterpolatedStringHandler.AppendFormatted(item25);
											defaultInterpolatedStringHandler.AppendLiteral(" esdbnd (targeting ");
											defaultInterpolatedStringHandler.AppendFormatted(text43);
											defaultInterpolatedStringHandler.AppendLiteral(" in ");
											defaultInterpolatedStringHandler.AppendFormatted(item23);
											defaultInterpolatedStringHandler.AppendLiteral(")");
											throw new Exception(defaultInterpolatedStringHandler.ToStringAndClear());
										}
										string text44 = binderFile2.Name.Replace(item25, item23).Replace(sourceName, text43);
										BinderFile item27 = new BinderFile(binderFile2.Flags, num129++, text44, binderFile2.Bytes);
										bnd3.Files.Add(item27);
									}
								}
							}
							Dictionary<string, ESD> dictionary36;
							if (CS$<>8__locals1.esds.TryGetValue(esdMap, out dictionary36))
							{
								foreach (BinderFile binderFile3 in bnd3.Files)
								{
									string key9 = GameEditor.BaseName(binderFile3.Name);
									ESD esd3;
									if (dictionary36.TryGetValue(key9, out esd3))
									{
										binderFile3.Bytes = esd3.Write();
									}
								}
							}
							string text45 = CS$<>8__locals1.outDir + "\\script\\talk\\" + esdMap + ".talkesdbnd.dcx";
							bnd3.Write(text45, CS$<>8__locals1.overrideDcx);
						}
					}
					goto IL_11522;
				}
			}
			string path2 = CS$<>8__locals1.outDir + "\\script\\talk";
			if (Directory.Exists(path2))
			{
				string[] array = Directory.GetFiles(path2, "*.talkesdbnd.dcx");
				for (int num96 = 0; num96 < array.Length; num96++)
				{
					File.Delete(array[num96]);
				}
			}
			foreach (KeyValuePair<string, Dictionary<string, ESD>> keyValuePair17 in CS$<>8__locals1.esds)
			{
				string text46 = CS$<>8__locals1.outDir + "\\script\\talk\\" + keyValuePair17.Key + ".talkesdbnd.dcx";
				CS$<>8__locals1.editor.OverrideBndRel<ESD>(item[keyValuePair17.Key], text46, keyValuePair17.Value, (ESD f) => f.Write(), null, CS$<>8__locals1.overrideDcx);
			}
			IL_11522:
			if (notify != null)
			{
				notify("Writing map data");
			}
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(13, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Writing ");
			defaultInterpolatedStringHandler.AppendFormatted<int>(CS$<>8__locals1.writeMsbs.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" maps");
			Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
			CS$<>8__locals1.writeOver = false;
			GameDataWriterE.<>c__DisplayClass1_0 CS$<>8__locals47 = CS$<>8__locals1;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(67, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Produced by Fog Gate Randomizer by thefifthmatt. Options and seed: ");
			defaultInterpolatedStringHandler.AppendFormatted<RandomizerOptions>(CS$<>8__locals1.opt);
			CS$<>8__locals47.optionsStr = defaultInterpolatedStringHandler.ToStringAndClear();
			ImmutableList.CreateRange<string>(CS$<>8__locals1.msbs.Keys);
			Parallel.ForEach<KeyValuePair<string, MSBE>>(CS$<>8__locals1.msbs, delegate(KeyValuePair<string, MSBE> entry)
			{
				if (!CS$<>8__locals1.writeOver && entry.Key.StartsWith("m6"))
				{
					Console.WriteLine("Writing overworld maps");
					CS$<>8__locals1.writeOver = true;
				}
				entry.Value.Events.Navmeshes.Add(new MSBE.Event.Navmesh
				{
					Name = CS$<>8__locals1.optionsStr,
					NavmeshRegionName = null
				});
				string path4 = CS$<>8__locals1.outDir + "\\map\\mapstudio\\" + entry.Key + ".msb.dcx";
				base.<Write>g__writeWithDupe|80(CS$<>8__locals1.writeMsbs, entry.Key, path4, delegate(string p)
				{
					entry.Value.Write(p, CS$<>8__locals1.overrideDcx);
				});
			});
			Console.WriteLine("Done");
		}

		// Token: 0x060000B8 RID: 184 RVA: 0x000236D8 File Offset: 0x000218D8
		[CompilerGenerated]
		internal static Vector3 <Write>g__getStrafeOffset|1_19(Vector3 rotation, float amt)
		{
			float num = (rotation.Y - 90f) * 3.1415927f / 180f;
			return new Vector3((float)Math.Sin((double)num) * amt, 0f, (float)Math.Cos((double)num) * amt);
		}

		// Token: 0x060000B9 RID: 185 RVA: 0x0002371C File Offset: 0x0002191C
		[CompilerGenerated]
		internal static List<float> <Write>g__parseFloats|1_20(IEnumerable<string> strs)
		{
			return (from c in strs
			select float.Parse(c, CultureInfo.InvariantCulture)).ToList<float>();
		}

		// Token: 0x060000BA RID: 186 RVA: 0x00023748 File Offset: 0x00021948
		[CompilerGenerated]
		internal static void <Write>g__addAssetModel|1_21(MSBE msb, string name)
		{
			if (!msb.Models.Assets.Any((MSBE.Model.Asset m) => m.Name == name))
			{
				List<MSBE.Model.Asset> assets = msb.Models.Assets;
				MSBE.Model.Asset asset = new MSBE.Model.Asset();
				asset.Name = name;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(48, 3);
				defaultInterpolatedStringHandler.AppendLiteral("N:\\GR\\data\\Asset\\Environment\\geometry\\");
				defaultInterpolatedStringHandler.AppendFormatted(name.Substring(0, 6));
				defaultInterpolatedStringHandler.AppendLiteral("\\");
				defaultInterpolatedStringHandler.AppendFormatted(name);
				defaultInterpolatedStringHandler.AppendLiteral("\\sib\\");
				defaultInterpolatedStringHandler.AppendFormatted(name);
				defaultInterpolatedStringHandler.AppendLiteral(".sib");
				asset.SibPath = defaultInterpolatedStringHandler.ToStringAndClear();
				assets.Add(asset);
			}
		}

		// Token: 0x060000BB RID: 187 RVA: 0x0002381C File Offset: 0x00021A1C
		[CompilerGenerated]
		internal static void <Write>g__addEnemyModel|1_22(MSBE msb, string name)
		{
			if (!msb.Models.Enemies.Any((MSBE.Model.Enemy m) => m.Name == name))
			{
				List<MSBE.Model.Enemy> enemies = msb.Models.Enemies;
				MSBE.Model.Enemy enemy = new MSBE.Model.Enemy();
				enemy.Name = name;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(30, 2);
				defaultInterpolatedStringHandler.AppendLiteral("N:\\GR\\data\\Model\\chr\\");
				defaultInterpolatedStringHandler.AppendFormatted(name);
				defaultInterpolatedStringHandler.AppendLiteral("\\sib\\");
				defaultInterpolatedStringHandler.AppendFormatted(name);
				defaultInterpolatedStringHandler.AppendLiteral(".sib");
				enemy.SibPath = defaultInterpolatedStringHandler.ToStringAndClear();
				enemies.Add(enemy);
			}
		}

		// Token: 0x060000BC RID: 188 RVA: 0x000238CC File Offset: 0x00021ACC
		[CompilerGenerated]
		internal static void <Write>g__setNameIdent|1_23(MSBE.Part part)
		{
			int unk;
			if (int.TryParse(part.Name.Split('_', StringSplitOptions.None).Last<string>(), out unk))
			{
				part.Unk08 = unk;
			}
		}

		// Token: 0x060000BD RID: 189 RVA: 0x000238FC File Offset: 0x00021AFC
		[CompilerGenerated]
		internal static bool <Write>g__setAssetName|1_24(MSBE.Part.Asset fog, string newName)
		{
			string name = fog.Name;
			fog.Name = newName;
			GameDataWriterE.<Write>g__setNameIdent|1_23(fog);
			bool result = true;
			for (int i = 0; i < fog.UnkPartNames.Length; i++)
			{
				string text = fog.UnkPartNames[i];
				if (text != null)
				{
					if (text == name)
					{
						fog.UnkPartNames[i] = fog.Name;
					}
					else
					{
						result = false;
					}
				}
			}
			if (fog.UnkT54PartName != null)
			{
				if (fog.UnkT54PartName == name)
				{
					fog.UnkT54PartName = fog.Name;
				}
				else
				{
					result = false;
				}
			}
			return result;
		}

		// Token: 0x060000BE RID: 190 RVA: 0x00023980 File Offset: 0x00021B80
		[CompilerGenerated]
		internal static void <Write>g__setBoxRegion|1_27(MSBE.Region r, string spec)
		{
			List<float> list = GameDataWriterE.<Write>g__parseFloats|1_20(spec.Split(' ', StringSplitOptions.None));
			r.Position = new Vector3(list[0], list[1], list[2]);
			r.Rotation = new Vector3(0f, list[3], 0f);
			r.Shape = new MSB.Shape.Box
			{
				Width = list[4],
				Height = list[5],
				Depth = list[6]
			};
		}

		// Token: 0x060000BF RID: 191 RVA: 0x00023A0C File Offset: 0x00021C0C
		[CompilerGenerated]
		internal static Vector3 <Write>g__moveInDirection|1_29(Vector3 v, Vector3 r, float dist)
		{
			float num = r.Y * 3.1415927f / 180f;
			return new Vector3(v.X + (float)Math.Sin((double)num) * dist, v.Y, v.Z + (float)Math.Cos((double)num) * dist);
		}

		// Token: 0x060000C0 RID: 192 RVA: 0x00023A5C File Offset: 0x00021C5C
		[CompilerGenerated]
		internal static Vector3 <Write>g__oppositeRotation|1_30(Vector3 vec)
		{
			float num = vec.Y + 180f;
			num = ((num >= 180f) ? (num - 360f) : num);
			return new Vector3(vec.X, num, vec.Z);
		}

		// Token: 0x060000C1 RID: 193 RVA: 0x00023A9B File Offset: 0x00021C9B
		[CompilerGenerated]
		internal static byte[] <Write>g__parseMap|1_32(string map)
		{
			return map.TrimStart('m').Split('_', StringSplitOptions.None).Select(new Func<string, byte>(byte.Parse)).ToArray<byte>();
		}

		// Token: 0x060000C2 RID: 194 RVA: 0x00023AC3 File Offset: 0x00021CC3
		[CompilerGenerated]
		internal static string <Write>g__formatMap|1_33(IEnumerable<byte> bytes)
		{
			return "m" + string.Join("_", bytes.Select(delegate(byte b)
			{
				if (b != 255)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(0, 1);
					defaultInterpolatedStringHandler.AppendFormatted<byte>(b, "d2");
					return defaultInterpolatedStringHandler.ToStringAndClear();
				}
				return "XX";
			}));
		}

		// Token: 0x060000C3 RID: 195 RVA: 0x00023B00 File Offset: 0x00021D00
		[CompilerGenerated]
		internal static string <Write>g__parentMap|1_34(byte[] mapBytes, bool top)
		{
			mapBytes = mapBytes.ToArray<byte>();
			if (top)
			{
				byte[] array = mapBytes;
				int num = 1;
				array[num] /= 4;
				byte[] array2 = mapBytes;
				int num2 = 2;
				array2[num2] /= 4;
				mapBytes[3] = 2;
			}
			else
			{
				byte[] array3 = mapBytes;
				int num3 = 1;
				array3[num3] /= 2;
				byte[] array4 = mapBytes;
				int num4 = 2;
				array4[num4] /= 2;
				mapBytes[3] = 1;
			}
			return GameDataWriterE.<Write>g__formatMap|1_33(mapBytes);
		}

		// Token: 0x060000C4 RID: 196 RVA: 0x00023B5C File Offset: 0x00021D5C
		[CompilerGenerated]
		internal static string <Write>g__getAltMap|1_35(string map)
		{
			byte[] array = GameDataWriterE.<Write>g__parseMap|1_32(map);
			if ((array[0] == 60 || array[0] == 61) && array[3] == 0)
			{
				array[3] = 10;
				return GameDataWriterE.<Write>g__formatMap|1_33(array);
			}
			throw new Exception("Invalid map for alternate map id: " + map);
		}

		// Token: 0x060000C5 RID: 197 RVA: 0x00023BA0 File Offset: 0x00021DA0
		[CompilerGenerated]
		internal static void <Write>g__setMapVars|1_163<T>(Dictionary<T, string> dict, string area, string desc, Func<string, T> parser)
		{
			if (desc == null)
			{
				return;
			}
			foreach (string arg in desc.Split(' ', StringSplitOptions.None))
			{
				dict[parser(arg)] = area;
			}
		}

		// Token: 0x060000C6 RID: 198 RVA: 0x00023BDC File Offset: 0x00021DDC
		[CompilerGenerated]
		internal static int <Write>g__roundBonusSoul|1_167(int val)
		{
			if (val > 100000)
			{
				val = (int)Math.Ceiling((double)val / 10000.0) * 10000;
			}
			else if (val > 10000)
			{
				val = (int)Math.Ceiling((double)val / 1000.0) * 1000;
			}
			else
			{
				val = (int)Math.Ceiling((double)val / 100.0) * 100;
			}
			val = Math.Max(val, 200);
			return val;
		}

		// Token: 0x060000C7 RID: 199 RVA: 0x00023C58 File Offset: 0x00021E58
		[CompilerGenerated]
		internal static void <Write>g__applyMult|1_173(string field, ref GameDataWriterE.<>c__DisplayClass1_24 A_1, ref GameDataWriterE.<>c__DisplayClass1_25 A_2)
		{
			int num = GameDataWriterE.<Write>g__roundBonusSoul|1_167((int)((uint)A_1.row[field].Value * A_2.mult));
			A_1.row[field].Value = num;
		}

		// Token: 0x060000C8 RID: 200 RVA: 0x00023CA2 File Offset: 0x00021EA2
		[CompilerGenerated]
		internal static AST.Expr <Write>g__eventFlag|1_57(int flag)
		{
			return AST.MakeFunction("f15", new object[]
			{
				flag
			});
		}

		// Token: 0x060000C9 RID: 201 RVA: 0x00023CBD File Offset: 0x00021EBD
		[CompilerGenerated]
		internal static bool <Write>g__cmdSetFlags|1_60(ESD.CommandCall c, int flag)
		{
			return c.CommandBank == 1 && c.CommandID == 11 && c.Arguments.Count == 2 && AST.DisassembleExpression(c.Arguments[0]).IsInt(flag);
		}

		// Token: 0x060000CA RID: 202 RVA: 0x00023CFC File Offset: 0x00021EFC
		[CompilerGenerated]
		internal static bool <Write>g__getExprFlag|1_61(AST.Expr expr, out int flagArg)
		{
			flagArg = 0;
			AST.FunctionCall functionCall = expr as AST.FunctionCall;
			return functionCall != null && functionCall.Name == "f15" && functionCall.Args.Count == 1 && functionCall.Args[0].TryAsInt(ref flagArg);
		}

		// Token: 0x060000CB RID: 203 RVA: 0x00023D4C File Offset: 0x00021F4C
		[CompilerGenerated]
		internal static bool <Write>g__exprChecksFlags|1_62(AST.Expr expr, int flag)
		{
			int num;
			return GameDataWriterE.<Write>g__getExprFlag|1_61(expr, out num) && num == flag;
		}

		// Token: 0x060000CC RID: 204 RVA: 0x00023D6C File Offset: 0x00021F6C
		[CompilerGenerated]
		internal static AST.Expr <Write>g__getWarpCond|1_196(AnnotationData.Entrance entrance)
		{
			AnnotationData.Side aside = entrance.ASide;
			AST.Expr expr = GameDataWriterE.<Write>g__eventFlag|1_57(aside.Warp.SitFlag);
			if (aside.WarpBonfireFlag > 0)
			{
				expr = new AST.BinaryExpr
				{
					Op = "&&",
					Lhs = expr,
					Rhs = GameDataWriterE.<Write>g__eventFlag|1_57(aside.WarpBonfireFlag)
				};
			}
			return expr;
		}

		// Token: 0x060000CD RID: 205 RVA: 0x00023DC4 File Offset: 0x00021FC4
		[CompilerGenerated]
		internal static AST.Expr <Write>g__alwaysExpr|1_197(bool state, int val)
		{
			return new AST.BinaryExpr
			{
				Op = (state ? "==" : "!="),
				Lhs = AST.MakeVal(val),
				Rhs = AST.MakeVal(val)
			};
		}

		// Token: 0x060000CE RID: 206 RVA: 0x00023E04 File Offset: 0x00022004
		[CompilerGenerated]
		internal static void <Write>g__addBonfireEntities|1_233(IEnumerable<MSBE.Part> parts, uint offset, ref GameDataWriterE.<>c__DisplayClass1_51 A_2)
		{
			foreach (MSBE.Part part in parts)
			{
				if (part.EntityID > 0U && part.EntityID % 10000U < 1000U)
				{
					A_2.bonfireEntities.Add(part.EntityID + offset);
				}
			}
		}

		// Token: 0x060000CF RID: 207 RVA: 0x00023E78 File Offset: 0x00022078
		[CompilerGenerated]
		internal static void <Write>g__cloneReverse|1_70(MSBE msb, MSBE.Part.ConnectCollision con)
		{
			MSBE.Part.ConnectCollision con2 = (MSBE.Part.ConnectCollision)con.DeepCopy();
			con2.UnkT0B = !con2.UnkT0B;
			while (msb.Parts.ConnectCollisions.Any((MSBE.Part.ConnectCollision c) => c.Name == con2.Name))
			{
				MSBE.Part.ConnectCollision con4 = con2;
				int unk = con4.Unk08;
				con4.Unk08 = unk + 1;
				MSBE.Entry con3 = con2;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 2);
				defaultInterpolatedStringHandler.AppendFormatted(con2.ModelName);
				defaultInterpolatedStringHandler.AppendLiteral("_");
				defaultInterpolatedStringHandler.AppendFormatted<int>(con2.Unk08, "d4");
				con3.Name = defaultInterpolatedStringHandler.ToStringAndClear();
			}
			msb.Parts.ConnectCollisions.Add(con2);
		}

		// Token: 0x060000D0 RID: 208 RVA: 0x00023F54 File Offset: 0x00022154
		[CompilerGenerated]
		internal static void <Write>g__cloneWhere|1_71(MSBE msb, Predicate<MSBE.Part.ConnectCollision> pred)
		{
			for (int i = msb.Parts.ConnectCollisions.Count - 1; i >= 0; i--)
			{
				if (pred(msb.Parts.ConnectCollisions[i]))
				{
					GameDataWriterE.<Write>g__cloneReverse|1_70(msb, msb.Parts.ConnectCollisions[i]);
				}
			}
		}

		// Token: 0x060000D1 RID: 209 RVA: 0x00023FB0 File Offset: 0x000221B0
		[CompilerGenerated]
		internal static string <Write>g__tId|1_246(int esdId)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(5, 1);
			defaultInterpolatedStringHandler.AppendLiteral("t");
			defaultInterpolatedStringHandler.AppendFormatted<int>(esdId, "d9");
			defaultInterpolatedStringHandler.AppendLiteral(".esd");
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}

		// Token: 0x0400007D RID: 125
		private static readonly List<string> dupeMsbs = new List<string>
		{
			"m60_11_09_12",
			"m60_11_13_12",
			"m60_21_20_11",
			"m60_22_18_11",
			"m60_22_19_11",
			"m60_22_26_11",
			"m60_22_27_11",
			"m60_23_18_11",
			"m60_23_19_11",
			"m60_23_21_11",
			"m60_23_26_11",
			"m60_23_27_11",
			"m60_44_36_10",
			"m60_44_37_10",
			"m60_44_38_10",
			"m60_44_39_10",
			"m60_44_52_10",
			"m60_44_53_10",
			"m60_44_54_10",
			"m60_44_55_10",
			"m60_45_36_10",
			"m60_45_37_10",
			"m60_45_38_10",
			"m60_45_39_10",
			"m60_45_52_10",
			"m60_45_53_10",
			"m60_45_54_10",
			"m60_45_55_10",
			"m60_46_36_10",
			"m60_46_37_10",
			"m60_46_38_10",
			"m60_46_39_10",
			"m60_46_52_10",
			"m60_46_53_10",
			"m60_46_54_10",
			"m60_46_55_10",
			"m60_47_36_10",
			"m60_47_37_10",
			"m60_47_38_10",
			"m60_47_39_10",
			"m60_47_52_10",
			"m60_47_53_10",
			"m60_47_54_10",
			"m60_47_55_10",
			"m61_11_11_12",
			"m61_22_22_11",
			"m61_22_23_11",
			"m61_23_22_11",
			"m61_23_23_11",
			"m61_44_44_10",
			"m61_44_45_10",
			"m61_44_46_10",
			"m61_44_47_10",
			"m61_45_44_10",
			"m61_45_45_10",
			"m61_45_46_10",
			"m61_45_47_10",
			"m61_46_44_10",
			"m61_46_45_10",
			"m61_46_46_10",
			"m61_46_47_10",
			"m61_47_44_10",
			"m61_47_45_10",
			"m61_47_46_10",
			"m61_47_47_10"
		};
	}
}
