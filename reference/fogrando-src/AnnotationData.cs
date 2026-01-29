using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SoulsIds;
using YamlDotNet.Serialization;

namespace FogMod;

public class AnnotationData
{
	public class ConfigAnnotation
	{
		public string Opt { get; set; }

		public string TrueOpt { get; set; }

		public void UpdateOptions(RandomizerOptions options)
		{
			if (Opt != null)
			{
				options[Opt] = false;
			}
			if (TrueOpt != null)
			{
				options[TrueOpt] = true;
			}
		}
	}

	public abstract class Taggable
	{
		[YamlIgnore]
		private string tags;

		[YamlIgnore]
		public List<string> TagList = new List<string>();

		public string Tags
		{
			get
			{
				return tags;
			}
			set
			{
				tags = value;
				if (tags == null)
				{
					TagList = new List<string>();
				}
				else
				{
					TagList = Tags.Split(' ').ToList();
				}
			}
		}

		public bool HasTag(string tag)
		{
			return TagList.Contains(tag);
		}

		public void AddTag(string tag)
		{
			if (tags == null)
			{
				Tags = tag;
			}
			else
			{
				Tags = tags + " " + tag;
			}
		}
	}

	public enum AreaMode
	{
		None,
		Base,
		DLC,
		Both
	}

	public class Area : Taggable
	{
		public string Name { get; set; }

		public string Text { get; set; }

		public string BossText { get; set; }

		public string Comment { get; set; }

		public string Req { get; set; }

		public string ScalingBase { get; set; }

		public string OpenArea { get; set; }

		public string DebugInfo { get; set; }

		public int DefeatFlag { get; set; }

		public int BossTrigger { get; set; }

		public int TrapFlag { get; set; }

		public string StakePos { get; set; }

		public int StakeRadius { get; set; }

		public string StakeRegions { get; set; }

		public string NearbyAsset { get; set; }

		public string BossPos { get; set; }

		public string BossTriggerArea { get; set; }

		public string Maps { get; set; }

		public string MainMaps { get; set; }

		public List<Side> To { get; set; }

		[YamlIgnore]
		public bool IsCore { get; set; }

		[YamlIgnore]
		public bool IsExcluded { get; set; }

		[YamlIgnore]
		public AreaMode Mode { get; set; }

		[YamlIgnore]
		public bool IsBoss
		{
			get
			{
				if (DefeatFlag <= 0)
				{
					return HasTag("boss");
				}
				return true;
			}
		}
	}

	public class EnemyCol
	{
		public string Col { get; set; }

		public string Area { get; set; }

		public List<string> Includes { get; set; }
	}

	public class Item : Taggable
	{
		public string Name { get; set; }

		public string ID { get; set; }

		public string Comment { get; set; }

		public string Area { get; set; }

		public List<string> MultiAreas { get; set; }
	}

	public class CustomStart
	{
		public string Name { get; set; }

		public string Area { get; set; }

		public string Respawn { get; set; }
	}

	public class Entrance : Taggable
	{
		public string Name { get; set; }

		public int ID { get; set; }

		public string Area { get; set; }

		public string DebugInfo { get; set; }

		public List<string> DebugInfos { get; set; }

		public string Text { get; set; }

		public string SpecialText { get; set; }

		public string Comment { get; set; }

		public int Location { get; set; }

		public string DoorCond { get; set; }

		public string DoorName { get; set; }

		public string Silo { get; set; }

		public float AdjustHeight { get; set; }

		public float Strafe { get; set; }

		public float Raise { get; set; }

		public string Extend { get; set; }

		public string SplitFrom { get; set; }

		public string MakeFrom { get; set; }

		public string RemoveDest { get; set; }

		public string PairWith { get; set; }

		public Side ASide { get; set; }

		public Side BSide { get; set; }

		[YamlIgnore]
		public bool IsFixed { get; set; }

		[YamlIgnore]
		public string FullName { get; set; }

		public List<Side> Sides()
		{
			List<Side> list = new List<Side>();
			if (ASide != null)
			{
				list.Add(ASide);
			}
			if (BSide != null)
			{
				list.Add(BSide);
			}
			return list;
		}

		public override string ToString()
		{
			return "Entrance[" + FullName + "]";
		}
	}

	public class Side : Taggable
	{
		public string Area { get; set; }

		public string FullArea { get; set; }

		public string Text { get; set; }

		public string Comment { get; set; }

		public int Flag { get; set; }

		public int TrapFlag { get; set; }

		public int EntryFlag { get; set; }

		public int BeforeWarpFlag { get; set; }

		[YamlIgnore]
		public List<int> OtherWarpFlags { get; set; }

		public string BossDefeatName { get; set; }

		public string BossTrapName { get; set; }

		public string BossTriggerName { get; set; }

		public int BossTrigger { get; set; }

		public string BossTriggerArea { get; set; }

		public string AltBossTriggerArea { get; set; }

		public int WarpFlag { get; set; }

		public int WarpBonfire { get; set; }

		public int WarpBonfireFlag { get; set; }

		public int WarpDefeatFlag { get; set; }

		public string WarpChest { get; set; }

		public string DestinationStake { get; set; }

		public string WarpObject { get; set; }

		public string StakeAsset { get; set; }

		public float StakeAssetDepth { get; set; }

		public string StakeRespawn { get; set; }

		public string StakeRegions { get; set; }

		public string TrimRegion { get; set; }

		public string NearbyEnemy { get; set; }

		public int Cutscene { get; set; }

		public string Cond { get; set; }

		public int IgnoreCondFlag { get; set; }

		public string CustomWarp { get; set; }

		public int CustomActionWidth { get; set; }

		public string Col { get; set; }

		public int ActionRegion { get; set; }

		public string ExcludeIfRandomized { get; set; }

		public string AlternateOf { get; set; }

		public string ReturnWarp { get; set; }

		public string DestinationMap { get; set; }

		public float AdjustHeight { get; set; }

		[YamlIgnore]
		public Expr Expr { get; set; }

		[YamlIgnore]
		public Graph.WarpPoint Warp { get; set; }

		[YamlIgnore]
		public Side AlternateSide { get; set; }

		[YamlIgnore]
		public int AlternateFlag { get; set; }

		[YamlIgnore]
		public string Silo { get; set; }

		[YamlIgnore]
		public string LinkedSilo { get; set; }

		[YamlIgnore]
		public bool IsCore { get; set; }

		[YamlIgnore]
		public bool IsPseudoCore { get; set; }

		[YamlIgnore]
		public bool IsWorld { get; set; }

		[YamlIgnore]
		public bool IsExcluded { get; set; }

		[YamlIgnore]
		public int SegmentIndex { get; set; } = -1;

		public override string ToString()
		{
			return "Side[" + Area + "]";
		}
	}

	public class GameObject : Taggable
	{
		public string Area { get; set; }

		public string ID { get; set; }

		public string Text { get; set; }
	}

	public class RetryPoint : Taggable
	{
		public string Map { get; set; }

		public string Name { get; set; }

		public int ID { get; set; }

		public string PlayerMap { get; set; }

		public List<string> DebugInfo { get; set; }

		public string Area { get; set; }

		public string Comment { get; set; }

		public string FullArea { get; set; }
	}

	public class OpenBonfire : Taggable
	{
		public string DebugText { get; set; }

		public int Flag { get; set; }

		public int Tier { get; set; }

		public string Area { get; set; }

		public string NearBoss { get; set; }

		public int EntityID { get; set; }

		public string EntityMap { get; set; }
	}

	public class CustomSegment : Taggable
	{
		public string Name { get; set; }

		public string Text { get; set; }

		public string Comment { get; set; }

		public SegmentSide Entrance { get; set; }

		public SegmentSide Exit { get; set; }
	}

	public class SegmentSide : Taggable
	{
		public string Area { get; set; }

		public string Text { get; set; }

		public string Comment { get; set; }

		public int BeforeWarpFlag { get; set; }

		public string Map { get; set; }

		public string Stake { get; set; }

		public string Link { get; set; }

		public string MakeFrom { get; set; }

		public string TrapChest { get; set; }

		public string NearbyEnemy { get; set; }

		[YamlIgnore]
		public Entrance CustomEntrance { get; set; }
	}

	public class CustomBonfire : Taggable
	{
		public string Map { get; set; }

		public string Text { get; set; }

		public string Comment { get; set; }

		public string Location { get; set; }

		public int Base { get; set; }

		public string Asset { get; set; }

		public string Enemy { get; set; }

		public string Player { get; set; }
	}

	public class CustomBarrier : Taggable
	{
		public string Map { get; set; }

		public string Comment { get; set; }

		public string Assets { get; set; }

		public string Start { get; set; }

		public string End { get; set; }
	}

	public class DungeonItem : Taggable
	{
		public string Map { get; set; }

		public string ExtraMap { get; set; }

		public List<string> DebugText { get; set; }

		public string OriginalText { get; set; }

		public string ItemLot { get; set; }

		public string ShopRange { get; set; }

		public int ShopEntity { get; set; }

		public int ObjectEntity { get; set; }

		public string HelperObjects { get; set; }

		public string Text { get; set; }

		public string Comment { get; set; }

		public string ToArea { get; set; }

		public string ToMap { get; set; }

		public string Location { get; set; }

		public string Base { get; set; }

		public string RemoveDest { get; set; }

		[YamlIgnore]
		public bool IsExcluded { get; set; }
	}

	public class MapSpec
	{
		public string Map { get; set; }

		public string Name { get; set; }

		public int Start { get; set; }

		public int End { get; set; }

		public static MapSpec Of(string Map, string Name, int Start, int End)
		{
			return new MapSpec
			{
				Map = Map,
				Name = Name,
				Start = Start,
				End = End
			};
		}
	}

	public class Expr
	{
		public static readonly Expr TRUE = new Expr(new List<Expr>());

		public static readonly Expr FALSE = new Expr(new List<Expr>(), every: false);

		private readonly List<Expr> exprs;

		private readonly bool every;

		private readonly string name;

		private readonly int count;

		public Expr(List<Expr> exprs, bool every = true, string name = null, int count = 0)
		{
			if (exprs.Count() > 0 && name != null)
			{
				throw new ArgumentException("Given subexpressions alongside a named variable");
			}
			if (count > 0 && every)
			{
				throw new ArgumentException("Given an expression count alongside every = true");
			}
			this.exprs = exprs;
			this.every = every;
			this.name = name;
			this.count = count;
		}

		public static Expr Named(string name)
		{
			return new Expr(new List<Expr>(), every: true, name);
		}

		public bool IsTrue()
		{
			if (name == null && exprs.Count() == 0)
			{
				return every;
			}
			return false;
		}

		public bool IsFalse()
		{
			if (name == null && exprs.Count() == 0)
			{
				return !every;
			}
			return false;
		}

		public SortedSet<string> FreeVars()
		{
			if (name != null)
			{
				return new SortedSet<string> { name };
			}
			return new SortedSet<string>(exprs.SelectMany((Expr e) => e.FreeVars()));
		}

		private bool Needs(string check)
		{
			if (check == name)
			{
				return true;
			}
			if (every)
			{
				return exprs.Any((Expr e) => e.Needs(check));
			}
			return exprs.All((Expr e) => e.Needs(check));
		}

		public Expr Substitute(Dictionary<string, Expr> config)
		{
			if (name != null)
			{
				if (config.ContainsKey(name))
				{
					return config[name].Substitute(config);
				}
				return this;
			}
			return new Expr(exprs.Select((Expr e) => e.Substitute(config)).ToList(), every, null, count);
		}

		private Expr Flatten(Func<string, IEnumerable<string>> nameMapper)
		{
			if (name != null)
			{
				return new Expr((from n in nameMapper(name)
					select Named(n)).ToList());
			}
			return null;
		}

		private int Count(Func<string, int> func)
		{
			if (name != null)
			{
				return func(name);
			}
			IEnumerable<int> source = exprs.Select((Expr e) => e.Count(func));
			if (!every)
			{
				if (count != 0)
				{
					return source.OrderByDescending((int s) => s).Take(count).Sum();
				}
				return source.Max();
			}
			return source.Sum();
		}

		public (List<string>, float) Cost(Func<string, float> cost)
		{
			if (name != null)
			{
				return (new List<string> { name }, cost(name));
			}
			if (exprs.Count == 0)
			{
				return (new List<string>(), 0f);
			}
			IEnumerable<(List<string>, float)> source = exprs.Select((Expr e) => e.Cost(cost));
			if (!every)
			{
				source = source.OrderBy(((List<string>, float) e) => e.Item2).Take((count == 0) ? 1 : count);
			}
			return source.Aggregate(((List<string>, float) c1, (List<string>, float) c2) => (c1.Item1.Concat(c2.Item1).ToList(), c1.Item2 + c2.Item2));
		}

		public Expr Simplify()
		{
			if (name != null)
			{
				return this;
			}
			List<Expr> list = new List<Expr>();
			HashSet<string> hashSet = new HashSet<string>();
			int num = count;
			foreach (Expr expr2 in exprs)
			{
				Expr expr = expr2.Simplify();
				if (expr.name != null)
				{
					if (!hashSet.Contains(expr.name))
					{
						hashSet.Add(expr.name);
						list.Add(expr);
					}
				}
				else if (num > 0 && expr.IsTrue())
				{
					num--;
				}
				else if (every == expr.every && num == 0)
				{
					list.AddRange(expr.exprs);
				}
				else
				{
					if (expr.exprs.Count() == 0)
					{
						return expr.every ? TRUE : FALSE;
					}
					list.Add(expr);
				}
			}
			if (list.Count() == 1)
			{
				return list[0];
			}
			return new Expr(list, every, null, num);
		}

		public override string ToString()
		{
			if (name != null)
			{
				return name;
			}
			if (exprs.Count() == 0)
			{
				if (!every)
				{
					return "false";
				}
				return "true";
			}
			string text = (every ? "AND" : ((count == 0) ? "OR" : $"OR{count}"));
			return "(" + string.Join(" " + text + " ", exprs) + ")";
		}

		internal static void TestExprs()
		{
			Expr expr = ParseExpr("OR3 one two three four five six seven");
			Console.WriteLine(expr);
			string[] array = new string[4] { "one", "four", "two", "three" };
			foreach (string text in array)
			{
				expr = expr.Substitute(new Dictionary<string, Expr> { [text] = TRUE });
				Console.Write(expr?.ToString() + " -> ");
				expr = expr.Simplify();
				Console.WriteLine(expr);
			}
		}
	}

	public class FogLocations
	{
		public List<KeyItemLoc> Items = new List<KeyItemLoc>();

		public List<EnemyLocArea> EnemyAreas = new List<EnemyLocArea>();

		public List<EnemyLoc> Enemies = new List<EnemyLoc>();
	}

	public class KeyItemLoc
	{
		public string Key { get; set; }

		public List<string> DebugText { get; set; }

		public string AArea { get; set; }

		public string Area { get; set; }

		public string ReqAreas { get; set; }

		public string Lots { get; set; }

		public string Shops { get; set; }

		[YamlIgnore]
		public string ActualArea => (Area ?? AArea).Split(' ')[0];
	}

	public class EnemyLocArea
	{
		public string Name { get; set; }

		public string Groups { get; set; }

		public string Cols { get; set; }

		public string MainMap { get; set; }

		public int ScalingTier { get; set; }
	}

	public class EnemyLoc
	{
		public string Map { get; set; }

		public string ID { get; set; }

		public string Col { get; set; }

		public string DebugText { get; set; }

		public string AArea { get; set; }

		public string Area { get; set; }

		[YamlIgnore]
		public string ActualArea => (Area ?? AArea).Split(' ')[0];
	}

	public class CustomInitConfig
	{
		public List<StartItem> StartItems { get; set; }

		public bool EnableStartingClasses { get; set; }

		public List<StartClass> StartingClasses { get; set; }
	}

	public class StartItem
	{
		public string Name { get; set; }

		public int Quantity { get; set; }
	}

	public class StartClass
	{
		public string Name { get; set; }

		public int Vigor { get; set; }

		public int Mind { get; set; }

		public int Endurance { get; set; }

		public int Strength { get; set; }

		public int Dexterity { get; set; }

		public int Intelligence { get; set; }

		public int Faith { get; set; }

		public int Arcane { get; set; }

		public List<string> Equipment { get; set; }
	}

	public class CustomSegmentConfig
	{
		public List<SegmentWeight> Types { get; set; }

		public List<SegmentAmount> Amounts { get; set; }

		public string FinalEntrance { get; set; }

		public bool EnableManualSegments { get; set; }

		public List<string> ManualSegments { get; set; }
	}

	public class SegmentWeight
	{
		public Graph.SegmentType Type { get; set; }

		public int Weight { get; set; }
	}

	public class SegmentAmount
	{
		[YamlIgnore]
		public SortedSet<string> TagSet;

		public List<Graph.SegmentType> Types { get; set; }

		public string Tags { get; set; }

		public string MinPercent { get; set; }

		public string MaxPercent { get; set; }

		[YamlIgnore]
		public int MinGates { get; set; } = -1;

		[YamlIgnore]
		public int MaxGates { get; set; } = -1;

		[YamlIgnore]
		public int AddedGates { get; set; }

		public bool MaxedOut()
		{
			if (MaxGates >= 0)
			{
				return AddedGates >= MaxGates;
			}
			return false;
		}

		public static SegmentAmount Of(Graph.SegmentType type)
		{
			return new SegmentAmount
			{
				Types = new List<Graph.SegmentType> { type }
			};
		}

		public override string ToString()
		{
			StringBuilder stringBuilder = new StringBuilder($"SegmentAmount[Min={MinGates},Max={MaxGates}");
			if (Types != null)
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder3 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
				handler.AppendLiteral(",Types=");
				handler.AppendFormatted(string.Join(',', Types));
				stringBuilder3.Append(ref handler);
			}
			if (TagSet != null)
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder4 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder2);
				handler.AppendLiteral(",Tags=");
				handler.AppendFormatted(string.Join(',', TagSet));
				stringBuilder4.Append(ref handler);
			}
			stringBuilder.Append("]");
			return stringBuilder.ToString();
		}
	}

	private class LiteConfig
	{
		public List<Area> Areas { get; set; } = new List<Area>();

		public List<Entrance> Warps { get; set; } = new List<Entrance>();

		public List<Entrance> Entrances { get; set; } = new List<Entrance>();

		public List<DungeonItem> DungeonItems { get; set; } = new List<DungeonItem>();
	}

	[YamlIgnore]
	public Dictionary<string, EnemyLocArea> EnemyAreas = new Dictionary<string, EnemyLocArea>();

	public static readonly List<MapSpec> DS1Specs = new List<MapSpec>
	{
		MapSpec.Of("m10_00_00_00", "depths", 1400, 1420),
		MapSpec.Of("m10_01_00_00", "parish", 1403, 1421),
		MapSpec.Of("m10_02_00_00", "firelink", 0, 0),
		MapSpec.Of("m11_00_00_00", "paintedworld", 1600, 1604),
		MapSpec.Of("m12_00_00_01", "darkroot", 2900, 2908),
		MapSpec.Of("m12_01_00_00", "dlc", 2909, 2920),
		MapSpec.Of("m13_00_00_00", "catacombs", 3951, 3954),
		MapSpec.Of("m13_01_00_00", "totg", 3961, 3964),
		MapSpec.Of("m13_02_00_00", "greathollow", 3970, 3971),
		MapSpec.Of("m14_00_00_00", "blighttown", 4950, 4956),
		MapSpec.Of("m14_01_00_00", "demonruins", 4960, 4973),
		MapSpec.Of("m15_00_00_00", "sens", 5150, 5153),
		MapSpec.Of("m15_01_00_00", "anorlondo", 5860, 5871),
		MapSpec.Of("m16_00_00_00", "newlondo", 6601, 6606),
		MapSpec.Of("m17_00_00_00", "dukes", 7900, 7908),
		MapSpec.Of("m18_00_00_00", "kiln", 8050, 8051),
		MapSpec.Of("m18_01_00_00", "asylum", 8950, 8953)
	};

	public static readonly List<MapSpec> DS3Specs = new List<MapSpec>
	{
		MapSpec.Of("m30_00_00_00", "highwall", 400, 402),
		MapSpec.Of("m30_01_00_00", "lothric", 400, 402),
		MapSpec.Of("m31_00_00_00", "settlement", 400, 402),
		MapSpec.Of("m32_00_00_00", "archdragon", 400, 402),
		MapSpec.Of("m33_00_00_00", "farronkeep", 400, 402),
		MapSpec.Of("m34_01_00_00", "archives", 400, 402),
		MapSpec.Of("m35_00_00_00", "cathedral", 400, 402),
		MapSpec.Of("m37_00_00_00", "irithyll", 400, 402),
		MapSpec.Of("m38_00_00_00", "catacombs", 400, 402),
		MapSpec.Of("m39_00_00_00", "dungeon", 400, 402),
		MapSpec.Of("m40_00_00_00", "firelink", 400, 402),
		MapSpec.Of("m41_00_00_00", "kiln", 400, 402),
		MapSpec.Of("m45_00_00_00", "ariandel", 400, 402),
		MapSpec.Of("m50_00_00_00", "dregheap", 400, 402),
		MapSpec.Of("m51_00_00_00", "ringedcity", 400, 402),
		MapSpec.Of("m51_01_00_00", "filianore", 400, 402)
	};

	public List<ConfigAnnotation> Options { get; set; }

	public float HealthScaling { get; set; }

	public float DamageScaling { get; set; }

	public List<Area> Areas { get; set; } = new List<Area>();

	public Dictionary<string, string> ConfigVars { get; set; }

	public List<Item> KeyItems { get; set; } = new List<Item>();

	public List<Entrance> Warps { get; set; } = new List<Entrance>();

	public List<Entrance> Entrances { get; set; } = new List<Entrance>();

	public List<GameObject> Objects { get; set; }

	public List<RetryPoint> RetryPoints { get; set; }

	public List<OpenBonfire> OpenBonfires { get; set; }

	public List<DungeonItem> DungeonItems { get; set; }

	public List<CustomSegment> CustomSegments { get; set; }

	public List<CustomBonfire> CustomBonfires { get; set; }

	public List<CustomBarrier> CustomBarriers { get; set; }

	public List<CustomStart> CustomStarts { get; set; }

	public Dictionary<string, float> DefaultCost { get; set; }

	public List<EnemyCol> Enemies { get; set; }

	public Dictionary<int, string> LotLocations { get; set; }

	public Dictionary<string, int> DefaultFlagCols { get; set; }

	[YamlIgnore]
	public FogLocations Locations { get; set; }

	[YamlIgnore]
	public CustomInitConfig InitConfig { get; set; }

	[YamlIgnore]
	public CustomSegmentConfig SegmentConfig { get; set; }

	[YamlIgnore]
	public Dictionary<string, MapSpec> Specs { get; set; }

	[YamlIgnore]
	public Dictionary<string, MapSpec> NameSpecs { get; set; }

	public void SetGame(GameSpec.FromGame game)
	{
		List<MapSpec> source;
		switch (game)
		{
		case GameSpec.FromGame.DS1:
		case GameSpec.FromGame.DS1R:
			source = DS1Specs;
			break;
		case GameSpec.FromGame.DS3:
			source = DS3Specs;
			break;
		default:
			return;
		}
		Specs = source.ToDictionary((MapSpec s) => s.Map, (MapSpec s) => s);
		NameSpecs = source.ToDictionary((MapSpec s) => s.Name, (MapSpec s) => s);
	}

	public static Expr ParseExpr(string s)
	{
		if (s == null)
		{
			return null;
		}
		if (!s.Contains(" )"))
		{
			return ParseSimpleExpr(s);
		}
		string message = "Internal error: badly formatted condition " + s;
		List<Expr> list = new List<Expr>();
		foreach (string item2 in s.Split(' ').Reverse())
		{
			Expr item;
			switch (item2)
			{
			case ")":
				item = null;
				break;
			default:
				if (!item2.StartsWith("OR"))
				{
					item = Expr.Named(item2);
					break;
				}
				goto case "AND";
			case "AND":
			{
				if (list.Count == 0)
				{
					throw new Exception(message);
				}
				int num = list.LastIndexOf(null);
				List<Expr> exprs = list.Skip(num + 1).Reverse().ToList();
				int num2 = Math.Max(0, num);
				list.RemoveRange(num2, list.Count - num2);
				if (item2 == "AND")
				{
					item = new Expr(exprs);
					break;
				}
				if (item2 == "OR")
				{
					item = new Expr(exprs, every: false);
					break;
				}
				if (item2.StartsWith("OR"))
				{
					item = new Expr(exprs, every: false, null, int.Parse(item2.Substring(2)));
					break;
				}
				throw new Exception(message);
			}
			case "(":
				continue;
			}
			list.Add(item);
		}
		if (list.Count == 1)
		{
			Expr expr = list[0];
			if (expr != null)
			{
				return expr;
			}
		}
		throw new Exception(message);
	}

	public static Expr ParseSimpleExpr(string s)
	{
		string[] array = s.Split(' ');
		string text = array[0];
		if (array.Length == 1)
		{
			if (text == "TRUE")
			{
				return Expr.TRUE;
			}
			if (text == "FALSE")
			{
				return Expr.FALSE;
			}
			return Expr.Named(text);
		}
		if (array.Length > 1 && text == "AND")
		{
			return new Expr((from w in array.Skip(1)
				select Expr.Named(w)).ToList());
		}
		if (array.Length > 1 && text == "OR")
		{
			return new Expr((from w in array.Skip(1)
				select Expr.Named(w)).ToList(), every: false);
		}
		if (array.Length > 1 && text.StartsWith("OR"))
		{
			return new Expr((from w in array.Skip(1)
				select Expr.Named(w)).ToList(), every: false, null, int.Parse(text.Substring(2)));
		}
		throw new Exception("Internal error: badly formatted condition " + s);
	}

	public static AnnotationData LoadLiteConfig(string path)
	{
		try
		{
			IDeserializer deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
			using StreamReader input = File.OpenText(path);
			LiteConfig liteConfig = deserializer.Deserialize<LiteConfig>(input);
			return new AnnotationData
			{
				Areas = liteConfig.Areas,
				Warps = liteConfig.Warps,
				Entrances = liteConfig.Entrances,
				DungeonItems = liteConfig.DungeonItems
			};
		}
		catch (Exception)
		{
			return null;
		}
	}
}
