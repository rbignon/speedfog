using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SoulsIds;

namespace FogMod;

public class GraphConnector
{
	private enum EdgeSilo
	{
		PAIRED,
		UNPAIRED
	}

	public enum CoreSelection
	{
		None,
		CoreOnly,
		PeripheryOnly
	}

	private readonly RandomizerOptions opt;

	private readonly Graph g;

	private readonly AnnotationData ann;

	public GraphConnector(RandomizerOptions opt, Graph g, AnnotationData ann)
	{
		this.opt = opt;
		this.g = g;
		this.ann = ann;
	}

	public void Connect()
	{
		bool flag = opt.Game != GameSpec.FromGame.ER;
		if (opt["crawl"] || opt["shuffle"] || flag)
		{
			ConnectRandom();
			return;
		}
		throw new Exception("Unknown mode (only World Shuffle supported)");
	}

	public void ConnectDungeons()
	{
		Regex regex = new Regex("m3[0-2]|m34_12|m10_01|m18_00");
		Regex regex2 = new Regex("m60");
		SortedDictionary<string, List<AnnotationData.Entrance>> sortedDictionary = new SortedDictionary<string, List<AnnotationData.Entrance>>();
		foreach (AnnotationData.Entrance entrance in ann.Entrances)
		{
			if (!entrance.HasTag("unused") && !entrance.IsFixed && entrance.ASide.AlternateOf == null && entrance.BSide.AlternateOf == null)
			{
				string text = entrance.ASide.DestinationMap ?? entrance.Area;
				string text2 = entrance.BSide.DestinationMap ?? entrance.Area;
				string text3 = text;
				if (regex.IsMatch(text2) || regex2.IsMatch(text))
				{
					text3 = text2;
				}
				if (regex.IsMatch(text3) || entrance.Silo != null)
				{
					Util.AddMulti(sortedDictionary, text3, entrance);
				}
			}
		}
		foreach (List<AnnotationData.Entrance> value in sortedDictionary.Values)
		{
			value.Sort((AnnotationData.Entrance a, AnnotationData.Entrance b) => entranceOrder(a).CompareTo(entranceOrder(b)));
		}
		Graph.Edge edge = null;
		Graph.Edge edge2 = null;
		foreach (AnnotationData.Entrance e in sortedDictionary.Values.SelectMany((List<AnnotationData.Entrance> es) => es))
		{
			string text4 = e.ASide.Area;
			string text5 = e.BSide.Area;
			if (e.ASide.Silo != null && e.BSide.Silo != null && e.BSide.Silo.StartsWith("from"))
			{
				string text6 = text5;
				text5 = text4;
				text4 = text6;
			}
			Graph.Edge edge3 = g.Nodes[text4].To.Find((Graph.Edge to) => to.Name == e.FullName);
			Graph.Edge? edge4 = g.Nodes[text5].To.Find((Graph.Edge to) => to.Name == e.FullName);
			if (edge3 == null)
			{
				throw new Exception("Missing " + e.FullName + " " + text4);
			}
			if (edge == null)
			{
				edge = edge3;
			}
			if (edge2 != null)
			{
				Console.WriteLine($"{edge3} --> {edge2}");
				g.Connect(edge3, edge2);
			}
			edge2 = edge4.Pair;
		}
		Console.WriteLine($"{edge} --> {edge2}");
		g.Connect(edge, edge2);
		static int entranceOrder(AnnotationData.Entrance entrance)
		{
			if (entrance.Silo == null)
			{
				return 1;
			}
			if (entrance.Silo.Contains("mini"))
			{
				return 2;
			}
			if (entrance.Silo.Contains("minor"))
			{
				return 3;
			}
			if (entrance.Silo.Contains("room"))
			{
				return 4;
			}
			return 1;
		}
	}

	public void ConnectRandom()
	{
		var (coreAreas, pseudoCore) = g.MarkCoreAreas();
		if (opt["openstart"])
		{
			g.TagOpenStart();
		}
		if (g.ExcludeMode != AnnotationData.AreaMode.None)
		{
			foreach (Graph.Node value30 in g.Nodes.Values)
			{
				foreach (Graph.Edge item2 in value30.To)
				{
					Graph.Edge fixedLink = item2.FixedLink;
					if (false)
					{
						Console.WriteLine($"   {item2}, paired with {fixedLink}.");
					}
					if (fixedLink != null && item2.Link == null && fixedLink.Link == null && (!dlcSide(item2.Side) || !dlcSide(fixedLink.Side)))
					{
						g.Connect(item2, fixedLink);
						makeFixed(fixedLink);
						if (item2.Pair != null)
						{
							makeFixed(item2.Pair);
						}
					}
					if (g.ExcludeMode == AnnotationData.AreaMode.Base && item2.Side.HasTag("dlchack"))
					{
						g.Connect(item2, item2.Pair);
						makeFixed(item2.Pair);
					}
					if (false)
					{
						Console.WriteLine($"-> {item2}, paired with {fixedLink}.");
					}
				}
			}
		}
		if (opt["crawl"])
		{
			foreach (Graph.Node value31 in g.Nodes.Values)
			{
				foreach (Graph.Edge item3 in value31.To)
				{
					Graph.Edge fixedLink2 = item3.FixedLink;
					if (fixedLink2 != null && item3.Link == null && fixedLink2.Link == null && !item3.Side.IsCore && !fixedLink2.Side.IsCore)
					{
						g.Connect(item3, fixedLink2);
						makeFixed(fixedLink2);
						if (item3.Pair != null)
						{
							makeFixed(item3.Pair);
						}
					}
				}
			}
		}
		if (opt["explainper"])
		{
			Console.WriteLine("Core areas: " + string.Join(", ", coreAreas) + "\nPseudocore: " + string.Join(" ", pseudoCore));
		}
		int lowAmount = 3;
		HashSet<string> lowConnection = new HashSet<string>(from node2 in g.Nodes.Values
			where node2.From.Count <= lowAmount
			select node2.Area);
		HashSet<string> highConnection = new HashSet<string>(from node2 in g.Nodes.Values
			where node2.To.Count >= 4
			select node2.Area);
		List<string> highlight = new List<string> { "farumazula_maliketh", "leyndell_erdtree", "rauhruins_romina" };
		lowConnection.RemoveWhere((string n) => highlight.Contains(n) || highConnection.Contains(n));
		List<Graph.Edge> list = g.Nodes.Values.SelectMany((Graph.Node node2) => node2.From.Where((Graph.Edge edge15) => edge15.From == null)).ToList();
		List<Graph.Edge> list2 = g.Nodes.Values.SelectMany((Graph.Node node2) => node2.To.Where((Graph.Edge edge15) => edge15.To == null)).ToList();
		Random random = new Random(opt.Seed);
		Util.Shuffle(random, list);
		Util.Shuffle(random, list2);
		if (opt.Game == GameSpec.FromGame.ER)
		{
			if (opt["crawl"])
			{
				_ = g.ExcludeMode;
			}
			list = list.OrderBy((Graph.Edge n) => (!highlight.Contains(n.To)) ? ((!lowConnection.Contains(n.To)) ? 1 : 0) : 2).ToList();
			list2 = list2.OrderBy((Graph.Edge n) => (!highConnection.Contains(n.From) || (n.Expr != null && !(n.Expr.ToString() == n.From))) ? 1 : 0).ToList();
			int num = list.Count((Graph.Edge edge15) => edge15.Side.IsCore && edge15.Pair == null);
			int num2 = list2.Count((Graph.Edge edge15) => edge15.Side.IsCore && edge15.Pair == null);
			if (num < num2)
			{
				throw new Exception($"Unexpected routing issue (may be caused by merged mod), insufficient {num} warp destinations for {num2} origins");
			}
			while (num > num2)
			{
				Graph.Edge edge = (from edge15 in list
					where edge15.Side.IsCore
					group edge15 by edge15.To into g
					select g.ToList()).SelectMany((List<Graph.Edge> es) => (es.Count <= 1) ? Array.Empty<Graph.Edge>() : es.Where((Graph.Edge edge15) => edge15.Pair == null)).FirstOrDefault();
				if (edge == null)
				{
					throw new Exception("Routing issue trying to add connections, potentially due to different item placements. If using item randomizer, use fewer important locations, or add more required gates to fog gate randomizer.");
				}
				edge.Side.IsCore = false;
				list.Remove(edge);
				num--;
			}
			if (opt["crawl"])
			{
				List<Graph.Edge> list3 = list.Where((Graph.Edge edge15) => edge15.Pair != null && isMinidungeonBoss(g.Areas[edge15.To]) && edge15.Side.Silo == "tominor").ToList();
				List<Graph.Edge> list4 = list2.Where((Graph.Edge edge15) => edge15.Pair != null && edge15.Side.HasTag("fakegaol")).ToList();
				Util.Shuffle(new Random(opt.Seed - 1), list3);
				Util.Shuffle(new Random(opt.Seed - 2), list4);
				if (list4.Count > list3.Count || opt["explain"])
				{
					Console.WriteLine("Areas: " + string.Join(" ", from edge15 in list
						where isMinidungeonBoss(g.Areas[edge15.To])
						select $"{edge15}:{edge15.Side.Silo}"));
					for (int num3 = 0; num3 < Math.Max(list3.Count, list4.Count); num3++)
					{
						Console.WriteLine($"Evergaol mapping {((num3 < list4.Count) ? list4[num3] : null)} -> {((num3 < list3.Count) ? list3[num3] : null)}");
					}
				}
				if (list4.Count > list3.Count)
				{
					throw new Exception($"Internal error: unexpected evergaol size {list4.Count}->{list3.Count}");
				}
				list3.RemoveRange(list4.Count, list3.Count - list4.Count);
				ConnectEdges(list3, list4, "fake evergaol");
				list = list.Except(list3.Concat(list4.Select((Graph.Edge edge15) => edge15.Pair))).ToList();
				list2 = list2.Except(list4.Concat(list3.Select((Graph.Edge edge15) => edge15.Pair))).ToList();
			}
			Graph.Edge edge2 = list2.Find((Graph.Edge edge15) => edge15.Side.HasTag("start"));
			Graph.Edge edge3 = list.Find((Graph.Edge edge15) => edge15.Side.IsCore && !edge15.Side.HasTag("start") && !edge15.Side.HasTag("avoidstart"));
			if (edge2 != null && edge3 != null)
			{
				list2.Remove(edge2);
				list2.Insert(0, edge2);
				list.Remove(edge3);
				list.Insert(0, edge3);
			}
			ConnectEdges(list.Where((Graph.Edge edge15) => edge15.Side.IsCore).ToList(), list2.Where((Graph.Edge edge15) => edge15.Side.IsCore).ToList(), "main");
			if (opt["affinity"])
			{
				foreach (string silo in new List<string> { "minor", "mini", "room" })
				{
					List<Graph.Edge> allTos = list.Where((Graph.Edge edge15) => !edge15.Side.IsCore && edge15.Side.Silo != null && edge15.Side.Silo == "to" + silo).ToList();
					List<Graph.Edge> allFroms = list2.Where((Graph.Edge edge15) => !edge15.Side.IsCore && edge15.Side.Silo != null && edge15.Side.Silo == "from" + silo).ToList();
					ConnectEdges(allTos, allFroms, silo + " silo");
				}
			}
			if (opt["isolas"])
			{
				list = list.Where((Graph.Edge edge15) => edge15.From == null && edge15.Pair != null && !coreAreas.Contains(edge15.To)).ToList();
				list2 = list2.Where((Graph.Edge edge15) => edge15.To == null && edge15.Pair != null && !coreAreas.Contains(edge15.From)).ToList();
				ConnectEdges(list, list2, "periphery");
			}
		}
		else
		{
			ConnectEdges(list, list2, null);
		}
		if (opt["start"])
		{
			g.Start = ann.CustomStarts[new Random(opt.Seed - 1).Next(ann.CustomStarts.Count)];
		}
		else if (g.Areas.ContainsKey("asylum"))
		{
			g.Start = new AnnotationData.CustomStart
			{
				Name = "Asylum",
				Area = "asylum",
				Respawn = "asylum 1812961"
			};
		}
		else if (g.Areas.ContainsKey("firelink_cemetery"))
		{
			g.Start = new AnnotationData.CustomStart
			{
				Name = "Cemetery of Ash",
				Area = "firelink_cemetery",
				Respawn = "firelink 1812961"
			};
		}
		else if (g.Areas.ContainsKey("chapel_start"))
		{
			g.Start = new AnnotationData.CustomStart
			{
				Name = "Chapel",
				Area = "chapel_start",
				Respawn = "m10_01_00_00 10012020"
			};
		}
		string start = g.Start.Area;
		int tries = 0;
		GraphChecker checker = new GraphChecker();
		GraphChecker.CheckRecord check = null;
		bool pairedOnly = !opt["unconnected"];
		List<string> unvisited = new List<string>();
		Dictionary<string, string> rootComponents = new Dictionary<string, string>();
		Dictionary<string, HashSet<string>> rootPreds = new Dictionary<string, HashSet<string>>();
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>();
		GraphChecker.CheckMode checkMode = GraphChecker.CheckMode.Full;
		bool flag = opt.Game == GameSpec.FromGame.ER;
		HashSet<string> expandedCore;
		List<string> ordering;
		Dictionary<string, HashSet<string>> dominators;
		HashSet<string> pairedPeriphery;
		int rounds;
		HashSet<Graph.Edge> nonEntrancePseudoCore;
		HashSet<Graph.Edge> peripheryExits;
		Dictionary<Graph.Edge, HashSet<Graph.Edge>> edgeEdges;
		HashSet<Graph.Edge> globalEdges;
		List<Graph.Edge> outboundDetached;
		List<Graph.Edge> inboundDetached;
		HashSet<Graph.Edge> connectedToClique;
		int maxRank;
		if (flag && opt["crawl"])
		{
			List<string> list5 = (from a in g.Areas.Values.Where(g.IsMajorScalingBoss)
				select a.Name).ToList();
			List<int> list6 = (from a in list5
				select ann.EnemyAreas[a].ScalingTier into x
				orderby x
				select x).ToList();
			for (int num4 = 0; num4 < g.UnlockTiers.Count; num4++)
			{
				int minAmount = g.UnlockTiers[num4];
				if (minAmount != -1)
				{
					int num5 = list6.FindIndex((int t) => t >= minAmount);
					if (num5 < 0)
					{
						throw new Exception($"Internal error: no major boss found with original tier >= {minAmount}");
					}
					g.ConfigExprs[$"tier{num4 + 1}"] = AnnotationData.ParseExpr($"OR{num5 + 1} {string.Join(" ", list5)}");
				}
			}
			HashSet<string> tried = null;
			while (tries++ < 1000)
			{
				if (opt["explain"])
				{
					Console.WriteLine($"------------------------ Try {tries} (crawl)");
				}
				check = checker.Check(opt, g, start, GraphChecker.CheckMode.Partial);
				unvisited = check.Unvisited.Intersect(coreAreas).ToList();
				if (unvisited.Count == 0)
				{
					break;
				}
				pairedOnly = SwapUnreachableEdge(check, unvisited, tries, pairedOnly, CoreSelection.CoreOnly, tried);
			}
			if (!opt["noconnect"] && unvisited.Count > 0)
			{
				throw new Exception($"Couldn't solve seed {opt.DisplaySeed} - try a different one or different options");
			}
			Console.WriteLine($"Main fixup done in {tries} tries");
			Graph.Edge edge4 = g.Nodes["outskirts"].To.Find((Graph.Edge edge15) => edge15.Name == "m60_43_50_00_AEG099_230_9000");
			Graph.Edge edge5 = g.Nodes["altus_tower"].From.Find((Graph.Edge edge15) => edge15.Name == "m34_12_00_00_AEG099_003_9001");
			if (edge4 != null && edge5 != null && edge4.Link == null && edge5.Link == null && edge4.FixedLink?.Link != null && edge5.FixedLink?.Link != null)
			{
				g.Connect(edge4, edge5);
			}
		}
		else if (flag)
		{
			while (true)
			{
				int num6 = tries;
				tries = num6 + 1;
				if (num6 >= 100)
				{
					break;
				}
				if (opt["explain"])
				{
					Console.WriteLine($"------------------------ Try {tries} (core)");
				}
				check = checker.Check(opt, g, start, GraphChecker.CheckMode.Partial);
				unvisited = check.Unvisited.Intersect(coreAreas).ToList();
				if (opt["noconnect"] || unvisited.Count == 0)
				{
					break;
				}
				pairedOnly = SwapUnreachableEdge(check, unvisited, tries, pairedOnly, CoreSelection.CoreOnly);
			}
			if (!opt["noconnect"] && unvisited.Count > 0)
			{
				throw new Exception($"Couldn't solve seed {opt.DisplaySeed} - try a different one or different options");
			}
			Console.WriteLine($"Main fixup done in {tries} tries");
			expandedCore = new HashSet<string>(coreAreas);
			expandedCore.UnionWith(pseudoCore.Keys);
			ordering = new List<string>();
			foreach (Graph.Node value32 in g.Nodes.Values)
			{
				strongVisit(value32.Area);
			}
			ordering.Reverse();
			foreach (string item4 in ordering)
			{
				strongAssign(item4, item4);
			}
			foreach (KeyValuePair<string, string> item5 in pseudoCore)
			{
				rootComponents[item5.Key] = rootComponents[item5.Value];
			}
			foreach (KeyValuePair<string, string> item6 in rootComponents)
			{
				string key = item6.Key;
				string value = item6.Value;
				if (!check.Records.TryGetValue(key, out var value2))
				{
					continue;
				}
				foreach (string item7 in value2.InEdge.Select((KeyValuePair<Graph.Edge, float> keyValuePair) => keyValuePair.Key.From).Intersect(value2.Visited))
				{
					if (rootComponents.TryGetValue(item7, out var value3))
					{
						_ = value3 != value;
					}
					Util.AddMulti(rootPreds, key, item7);
				}
			}
			dominators = new Dictionary<string, HashSet<string>>();
			foreach (string key5 in rootPreds.Keys)
			{
				domVisit(key5);
			}
			SortedDictionary<string, List<string>> sortedDictionary = new SortedDictionary<string, List<string>>();
			foreach (IGrouping<string, KeyValuePair<string, string>> item8 in from keyValuePair in rootComponents
				group keyValuePair by keyValuePair.Value)
			{
				List<string> list7 = item8.Select((KeyValuePair<string, string> keyValuePair) => keyValuePair.Key).ToList();
				sortedDictionary[item8.Key] = list7;
				int num7 = list7.Sum((string a) => g.Nodes[a].To.Count((Graph.Edge edge15) => edge15.To == null || edge15.From == null));
				if (num7 <= 0)
				{
					continue;
				}
				int num8 = list7.Sum((string a) => g.Nodes[a].To.Count((Graph.Edge edge15) => (edge15.To == null || edge15.From == null) && edge15.Pair != null));
				if (opt["explainper"])
				{
					Console.WriteLine($"{item8.Key}: {string.Join(",", list7)} ({num8} paired, {num7 - num8} unpaired)");
				}
			}
			List<Graph.Edge> list8 = new List<Graph.Edge>();
			List<Graph.Edge> list9 = new List<Graph.Edge>();
			List<Graph.Edge> list10 = new List<Graph.Edge>();
			pairedPeriphery = new HashSet<string>(coreAreas);
			SortedDictionary<string, List<Graph.Edge>> sortedDictionary2 = new SortedDictionary<string, List<Graph.Edge>>();
			foreach (Graph.Node node in g.Nodes.Values)
			{
				if (g.Areas[node.Area].IsExcluded)
				{
					continue;
				}
				if (expandedCore.Contains(node.Area))
				{
					List<Graph.Edge> list11 = node.To.Where((Graph.Edge edge15) => edge15.Pair != null && edge15.To == null).ToList();
					list8.AddRange(list11);
					Util.AddMulti(sortedDictionary2, rootComponents[node.Area], list11);
					if (!coreAreas.Contains(node.Area) && list11.Count > 0)
					{
						pairedPeriphery.Add(node.Area);
					}
					continue;
				}
				List<Graph.Edge> list12 = node.From.Where((Graph.Edge edge15) => edge15.Pair != null && edge15.From == null).ToList();
				if (node.From.Any((Graph.Edge edge15) => edge15.IsWorld && !node.To.Any((Graph.Edge edge16) => edge16.To == edge15.From)))
				{
					list10.AddRange(list12);
				}
				else
				{
					list9.AddRange(list12);
				}
				if (list12.Count > 0)
				{
					pairedPeriphery.Add(node.Area);
				}
			}
			rounds = 0;
			Util.Shuffle(new Random(opt.Seed - rounds++), list9);
			list9.AddRange(list10);
			Util.Shuffle(new Random(opt.Seed - rounds++), list8);
			if (list8.Count > list9.Count)
			{
				throw new Exception($"Internal error: more unattached gates found in required areas ({list8.Count}) than optional areas ({list9.Count}), which is not handled yet. It may help to select \"Minor sending gates\" or other combination of options");
			}
			int num9 = Math.Min(list9.Count, list8.Count);
			for (int num10 = 0; num10 < num9; num10++)
			{
				Graph.Edge edge6 = list8[num10];
				Graph.Edge edge7 = list9[num10];
				if (opt["explainper"])
				{
					Console.WriteLine($"Random connect outbound {edge6} -> inbound {edge7}");
				}
				g.Connect(edge6, edge7);
			}
			if (list9.Count > num9)
			{
				List<Graph.Edge> list13 = list9.Skip(num9).ToList();
				List<Graph.Edge> list14 = list13.Select((Graph.Edge edge15) => edge15.Pair).ToList();
				Util.Shuffle(new Random(opt.Seed - rounds++), list13);
				Util.Shuffle(new Random(opt.Seed - rounds++), list14);
				ConnectEdges(list13, list14, "non-main");
			}
			connectPairedPeriphery("initial");
			Dictionary<Graph.Edge, List<Graph.Edge>> dictionary2 = new Dictionary<Graph.Edge, List<Graph.Edge>>();
			nonEntrancePseudoCore = new HashSet<Graph.Edge>();
			peripheryExits = new HashSet<Graph.Edge>();
			foreach (Graph.Node value33 in g.Nodes.Values)
			{
				string area = value33.Area;
				if (!expandedCore.Contains(area))
				{
					continue;
				}
				HashSet<string> visited = new HashSet<string>();
				List<Graph.Edge> list15 = new List<Graph.Edge>();
				foreach (Graph.Edge item9 in value33.To)
				{
					if (item9.To != null && !expandedCore.Contains(item9.To) && item9.Pair != null)
					{
						List<Graph.Edge> list16 = new List<Graph.Edge>();
						Graph.Edge pair = item9.Link.Pair;
						bool track = false;
						crossVisit(pair, item9, visited, list16, track);
						dictionary2[pair] = list16;
						list15.AddRange(list16);
					}
				}
				if (list15.Count > 0)
				{
					List<string> value4 = list15.Select((Graph.Edge edge15) => edge15.To).ToList();
					dictionary[area] = value4;
				}
			}
			edgeEdges = new Dictionary<Graph.Edge, HashSet<Graph.Edge>>();
			foreach (List<Graph.Edge> value34 in dictionary2.Values)
			{
				foreach (Graph.Edge item10 in value34)
				{
					Util.AddMulti(edgeEdges, item10, value34);
				}
			}
			globalEdges = new HashSet<Graph.Edge>();
			List<List<Graph.Edge>> list17 = new List<List<Graph.Edge>>();
			List<Graph.Edge> list18 = new List<Graph.Edge>();
			outboundDetached = new List<Graph.Edge>();
			inboundDetached = new List<Graph.Edge>();
			foreach (Graph.Edge key6 in edgeEdges.Keys)
			{
				if (globalEdges.Contains(key6))
				{
					continue;
				}
				List<Graph.Edge> list19 = new List<Graph.Edge>();
				visitEdge(key6, list19);
				List<Graph.Edge> list20 = (from edge15 in list19
					select edge15.Pair into edge15
					where edge15 != null && !edge15.IsFixed
					select edge15).ToList();
				if (list20.Count == 0)
				{
					continue;
				}
				if (list19.Count > 1)
				{
					list17.Add(list20);
					foreach (Graph.Edge item11 in list20)
					{
						outboundDetached.Add(item11.Link);
						g.Disconnect(item11.Link);
					}
				}
				else
				{
					list18.AddRange(list20);
				}
			}
			connectedToClique = new HashSet<Graph.Edge>();
			rounds = 0;
			List<List<Graph.Edge>> list21 = sortedDictionary2.Values.Where((List<Graph.Edge> g) => g.Count > 1).ToList();
			Util.Shuffle(new Random(opt.Seed - rounds++), list21);
			list21 = list21.OrderBy((List<Graph.Edge> g) => (!g.Intersect(nonEntrancePseudoCore).Any()) ? 1 : 0).ToList();
			List<List<Graph.Edge>> list22 = list17.OrderByDescending((List<Graph.Edge> l) => l.Count()).ToList();
			for (int num11 = 0; num11 < list22.Count; num11++)
			{
				List<Graph.Edge> list23 = list22[num11];
				if (opt["explainper"])
				{
					Console.WriteLine($"Clique counts: outbound {outboundDetached.Count}, misc inbound {inboundDetached.Count}, remaining [{string.Join(",", from c in list17.Skip(num11)
						select c.Count)}]");
				}
				List<Graph.Edge> list24 = null;
				foreach (List<Graph.Edge> item12 in list21)
				{
					List<Graph.Edge> list25 = item12.Where((Graph.Edge edge15) => !connectedToClique.Contains(edge15) && edge15 != edge15.Pair?.Link).ToList();
					if (list25.Count >= list23.Count)
					{
						connectAllToClique(list23, list25);
						list24 = item12;
						break;
					}
				}
				if (list24 != null)
				{
					if (!list24.Any((Graph.Edge item2) => nonEntrancePseudoCore.Contains(item2)) && list21.Remove(list24))
					{
						list21.Add(list24);
					}
					continue;
				}
				List<List<string>> list26 = sortedDictionary.Values.ToList();
				Util.Shuffle(new Random(opt.Seed - rounds++), list26);
				List<string> handleFirst = nonEntrancePseudoCore.Select((Graph.Edge edge15) => edge15.From).ToList();
				foreach (List<string> item13 in list26.OrderBy((List<string> g) => (!g.Intersect(handleFirst).Any()) ? 1 : 0))
				{
					List<Graph.Edge> list27 = (from edge15 in item13.SelectMany((string key5) => g.Nodes[key5].To)
						where !connectedToClique.Contains(edge15) && edge15.Pair != null && !edge15.Side.IsCore && edge15 != edge15.Pair?.Link
						select edge15).ToList();
					if (list27.Count >= list23.Count)
					{
						connectAllToClique(list23, list27);
						list24 = list27;
						break;
					}
				}
				if (list24 == null)
				{
					throw new Exception("Unsolvable seed: no core areas found for periphery clique " + string.Join(" ", list23));
				}
			}
			if (outboundDetached.Count != inboundDetached.Count)
			{
				throw new Exception($"Internal error: mismatched clique edge counts, outbound {outboundDetached.Count} -> inbound {inboundDetached.Count}");
			}
			for (int num12 = 0; num12 < outboundDetached.Count; num12++)
			{
				Graph.Edge exit = outboundDetached[num12];
				Graph.Edge entrance = inboundDetached[num12];
				g.Connect(exit, entrance);
			}
			if (!opt["noconnectper"])
			{
				connectPairedPeriphery("fixup");
				Console.WriteLine($"Clique fixup done in {tries}");
			}
			maxRank = 1000;
			List<Graph.Edge> list28 = g.Nodes.Values.SelectMany((Graph.Node node2) => node2.To.Where((Graph.Edge edge15) => edge15.To == null && edge15.Pair == null)).ToList();
			List<Graph.Edge> list29 = g.Nodes.Values.SelectMany((Graph.Node node2) => node2.From.Where((Graph.Edge edge15) => edge15.From == null && edge15.Pair == null)).ToList();
			if (list28.Count != list29.Count)
			{
				throw new Exception($"Internal error: mismatched remaining warps {list28.Count} -> {list29.Count}");
			}
			Dictionary<(Graph.Edge, Graph.Edge), int> dictionary3 = new Dictionary<(Graph.Edge, Graph.Edge), int>();
			int num13 = 995;
			for (int num14 = 0; num14 < list29.Count; num14++)
			{
				Graph.Edge edge8 = list29[num14];
				for (int num15 = 0; num15 < list28.Count; num15++)
				{
					Graph.Edge edge9 = list28[num15];
					int num16 = getRanking(edge9.From, edge8.To);
					if (edge9.Name == edge8.Name && num16 > num13)
					{
						num16 = num13;
					}
					dictionary3[(edge9, edge8)] = num16;
				}
			}
			Dictionary<Graph.Edge, List<Graph.Edge>> dictionary4 = (from keyValuePair in dictionary3
				group keyValuePair by keyValuePair.Key.Item2).ToDictionary((IGrouping<Graph.Edge, KeyValuePair<(Graph.Edge, Graph.Edge), int>> g) => g.Key, (IGrouping<Graph.Edge, KeyValuePair<(Graph.Edge, Graph.Edge), int>> g) => (from keyValuePair in g
				orderby keyValuePair.Value
				select keyValuePair.Key.Item1).ToList());
			Dictionary<Graph.Edge, Graph.Edge> dictionary5 = new Dictionary<Graph.Edge, Graph.Edge>();
			Dictionary<Graph.Edge, Graph.Edge> warpTargetMap = new Dictionary<Graph.Edge, Graph.Edge>();
			while (warpTargetMap.Count < list29.Count)
			{
				Graph.Edge edge10 = list29.Find((Graph.Edge key5) => !warpTargetMap.ContainsKey(key5));
				if (edge10 == null || !dictionary4.TryGetValue(edge10, out var value5) || value5.Count == 0)
				{
					throw new Exception("Internal error: Gale-Shapley bad state");
				}
				Graph.Edge edge11 = value5[value5.Count - 1];
				value5.RemoveAt(value5.Count - 1);
				if (dictionary5.TryGetValue(edge11, out var value6))
				{
					if (dictionary3[(edge11, edge10)] <= dictionary3[(edge11, value6)])
					{
						continue;
					}
					warpTargetMap.Remove(value6);
				}
				dictionary5[edge11] = edge10;
				warpTargetMap[edge10] = edge11;
			}
			Dictionary<Graph.Edge, List<Graph.Edge>> dictionary6 = new Dictionary<Graph.Edge, List<Graph.Edge>>();
			foreach (KeyValuePair<Graph.Edge, Graph.Edge> item14 in dictionary5)
			{
				Graph.Edge from = item14.Key;
				Graph.Edge to = item14.Value;
				int num17 = dictionary3[(from, to)];
				float num18 = 0f;
				if (check.Records.TryGetValue(from.From, out var value7) && check.Records.TryGetValue(to.To, out var value8))
				{
					num18 = value8.Dist - value7.Dist;
				}
				if (opt["explainper"])
				{
					Console.WriteLine($"gale {item14.Key} -> {item14.Value}, rank {num17}, {maybeDist(from.From)}->{maybeDist(to.To)}, {num18}");
				}
				if (num17 > 980 && num18 < 10f)
				{
					g.Connect(from, to);
					continue;
				}
				List<Graph.Edge> list30 = (from keyValuePair in dictionary3
					where keyValuePair.Key.Item1 == @from && keyValuePair.Value > 980
					orderby keyValuePair.Value descending
					select keyValuePair.Key.Item2).ToList();
				if (list30.Count == 0)
				{
					throw new Exception($"Fix me: No reasonable targets for {from}->{to}");
				}
				dictionary6[from] = list30;
				if (opt["explainper"])
				{
					Console.WriteLine("  alts: " + string.Join(" ", from keyValuePair in dictionary3
						where keyValuePair.Key.Item1 == @from && keyValuePair.Value > 980
						select $"{keyValuePair.Key.Item2.To}={keyValuePair.Value}"));
				}
				float maybeDist(string a)
				{
					if (!check.Records.TryGetValue(to.To, out var value28))
					{
						return -1f;
					}
					return value28.Dist;
				}
			}
			if (dictionary6.Count > 0)
			{
				checkMode = GraphChecker.CheckMode.FullForward;
				HashSet<Graph.Edge> duplicatedTargets = new HashSet<Graph.Edge>();
				foreach (KeyValuePair<Graph.Edge, List<Graph.Edge>> item15 in dictionary6.OrderBy((KeyValuePair<Graph.Edge, List<Graph.Edge>> keyValuePair) => keyValuePair.Value.Count))
				{
					Graph.Edge key2 = item15.Key;
					Graph.Edge edge12 = item15.Value.Find((Graph.Edge item2) => !duplicatedTargets.Contains(item2));
					if (edge12 == null)
					{
						edge12 = item15.Value[0];
					}
					duplicatedTargets.Add(edge12);
					if (opt["explainper"])
					{
						Console.WriteLine($"dupe {key2} -> {edge12}");
					}
					if (edge12.Link != null)
					{
						edge12 = g.DuplicateEntrance(edge12);
					}
					g.Connect(key2, edge12);
				}
			}
			Console.WriteLine("Done with core pass");
		}
		List<string> triedSwaps = new List<string>();
		tries = 0;
		pairedOnly = !opt["unconnected"];
		bool flag2 = opt["noconnect2"] || opt["crawl"];
		while (tries++ < 100 && !flag2)
		{
			if (opt["explain"])
			{
				Console.WriteLine($"------------------------ Try {tries}");
			}
			check = checker.Check(opt, g, start, checkMode);
			unvisited = check.Unvisited.ToList();
			if (opt["noconnect"])
			{
				break;
			}
			if (unvisited.Count == 0 && g.Areas.ContainsKey("firelink_cemetery"))
			{
				if (!MoveAreaEarlier(check, triedSwaps))
				{
					break;
				}
				continue;
			}
			if (unvisited.Count == 0)
			{
				break;
			}
			pairedOnly = SwapUnreachableEdge(check, unvisited, tries, pairedOnly, flag ? CoreSelection.PeripheryOnly : CoreSelection.None);
		}
		if (!opt["noconnect"] && !opt["noconnect2"] && unvisited.Count > 0)
		{
			throw new Exception($"Couldn't solve seed {opt.DisplaySeed} - try a different one");
		}
		Console.WriteLine();
		g.ConfigExprs["scalepass"] = AnnotationData.Expr.TRUE;
		g.ConfigExprs["logicpass"] = AnnotationData.Expr.FALSE;
		foreach (AnnotationData.Item keyItem in ann.KeyItems)
		{
			if (keyItem.HasTag("randomonly"))
			{
				g.ConfigExprs.Remove(keyItem.Name);
			}
		}
		GraphChecker.CheckRecord checkRecord = checker.Check(opt, g, start, flag2 ? GraphChecker.CheckMode.Partial : checkMode);
		if (checkRecord.Unvisited.Count == 0)
		{
			check = checkRecord;
		}
		else
		{
			_ = check.Unvisited.Count;
		}
		PostProcess(check, coreAreas);
		string text = "Graphviz\\bin\\dot.exe";
		if (!File.Exists(text))
		{
			text = "eldendata\\Graphviz\\dot.exe";
		}
		if (opt.GraphFilePath != null && File.Exists(text))
		{
			Console.WriteLine("Writing graph.dot because " + text + " was detected");
			TextWriter textWriter = File.CreateText("graph.dot");
			textWriter.WriteLine("digraph {");
			textWriter.WriteLine("  tooltip=\" \";");
			textWriter.WriteLine("  nodesep=0.25;");
			textWriter.WriteLine("  ranksep=0.7;");
			textWriter.WriteLine("  node [ fontsize=16,width=0.1,height=0.1 ];");
			bool flag3 = !opt["showpart"];
			Dictionary<string, int> dictionary7 = new Dictionary<string, int>();
			foreach (Graph.Node value35 in g.Nodes.Values)
			{
				string area2 = value35.Area;
				if ((!flag3 || coreAreas.Contains(area2)) && (check.Records.ContainsKey(area2) || !g.Areas[area2].HasTag("optional")) && (opt["showexcluded"] || !g.Areas[area2].IsExcluded))
				{
					string value9 = ((g.Areas[area2].HasTag("overworld") && !opt["crawl"]) ? "box3d" : "box");
					string text2 = g.Areas[area2].Text;
					if (g.AreaTiers.TryGetValue(area2, out var value10))
					{
						text2 += $" ({value10})";
					}
					int count = dictionary7.Count;
					dictionary7[value35.Area] = count;
					textWriter.WriteLine($"    \"{count}\" [ shape={value9},label=\"{escape(text2)}\",tooltip=\" \" ];");
				}
			}
			new HashSet<Graph.Connection>();
			HashSet<Graph.Edge> hashSet = new HashSet<Graph.Edge>();
			foreach (Graph.Node value36 in g.Nodes.Values)
			{
				foreach (Graph.Edge e in value36.To)
				{
					if (string.IsNullOrEmpty(e.From) || string.IsNullOrEmpty(e.To) || !dictionary7.ContainsKey(e.From) || !dictionary7.ContainsKey(e.To) || (flag3 && (!coreAreas.Contains(e.From) || !coreAreas.Contains(e.To))))
					{
						continue;
					}
					string text3 = "";
					new Graph.Connection(e.From, e.To);
					Graph.Edge edge13 = null;
					if (e.Link.Pair != null && simpleLink(e) == simpleLink(e.Link.Pair))
					{
						edge13 = e.Link.Pair;
					}
					else if (e.IsWorld && simpleLink(e))
					{
						List<Graph.Edge> list31 = g.Nodes[e.To].To.Where((Graph.Edge f) => f.To == e.From && simpleLink(f)).ToList();
						if (list31.Count == 1)
						{
							edge13 = list31[0];
						}
					}
					if (edge13 != null)
					{
						if (hashSet.Contains(e) && hashSet.Contains(edge13))
						{
							continue;
						}
						hashSet.Add(e);
						hashSet.Add(edge13);
						text3 = ",dir=both";
					}
					string value11 = (e.IsFixed ? "dashed" : "solid");
					string text4 = "";
					string value12 = "";
					string o = " ";
					if (e.LinkedExpr != null)
					{
						List<string> list32 = (from v in e.LinkedExpr.Substitute(g.ConfigExprs).FreeVars()
							where g.ItemAreas.ContainsKey(v)
							select v).Distinct().ToList();
						if (list32.Count > 0)
						{
							o = string.Join(" ", list32) + ", found in " + string.Join("; ", from a in list32.SelectMany((string v) => g.ItemAreas[v]).Distinct()
								select g.Areas[a].Text);
							value12 = ",color=red";
							text4 = "(item)";
						}
					}
					string text5 = e.From;
					string text6 = e.To;
					float num19 = 0f;
					if (check.Records.ContainsKey(e.To) && check.Records.ContainsKey(e.From))
					{
						num19 = check.Records[e.To].Dist - check.Records[e.From].Dist;
					}
					if (num19 < 0f)
					{
						string text7 = text6;
						text6 = text5;
						text5 = text7;
						if (text3 == "")
						{
							text3 = ",dir=back";
						}
						num19 *= -1f;
					}
					string value13 = (string.IsNullOrWhiteSpace(text4) ? "" : (",labelloc=t,label=\"" + escape(text4) + "\""));
					string value14 = $"  \"{dictionary7[text5]}\" -> \"{dictionary7[text6]}\" [ style={value11},labeltooltip=\"{escape(o)}\"{value13}{value12}{text3} ];";
					textWriter.WriteLine(value14);
				}
			}
			textWriter.WriteLine("}");
			textWriter.Close();
			try
			{
				List<string> list33 = new List<string> { "-Tsvg", "graph.dot", "-o", opt.GraphFilePath };
				Console.WriteLine("$ " + text + " " + string.Join(" ", list33));
				using Process process = new Process();
				process.StartInfo.FileName = text;
				foreach (string item16 in list33)
				{
					process.StartInfo.ArgumentList.Add(item16);
				}
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.CreateNoWindow = true;
				process.Start();
				process.WaitForExit();
			}
			catch (Exception value15)
			{
				Console.WriteLine(value15);
			}
			Console.WriteLine();
		}
		else
		{
			if (!opt["dumpgraph"])
			{
				return;
			}
			Console.WriteLine("Writing fog.dot");
			bool flag4 = true;
			TextWriter textWriter2 = File.CreateText("fog.dot");
			textWriter2.WriteLine((flag4 ? "di" : "") + "graph {");
			textWriter2.WriteLine("  nodesep=0.2;");
			textWriter2.WriteLine("  ranksep=0.5;");
			string value16 = "";
			textWriter2.WriteLine("  node [ fontsize=16,width=0.1,height=0.1 ];");
			List<string> list34 = (from keyValuePair in g.ItemAreas
				where keyValuePair.Key.StartsWith("rune")
				select keyValuePair.Value[0]).ToList();
			if (opt["rootpred"])
			{
				foreach (KeyValuePair<string, HashSet<string>> item17 in rootPreds)
				{
					string key3 = item17.Key;
					foreach (string pred in item17.Value)
					{
						string value17 = (g.Nodes[key3].To.Any((Graph.Edge edge15) => edge15.To == pred) ? "solid" : "dotted");
						string value18 = "";
						if (Math.Abs(check.Records[pred].Dist - check.Records[key3].Dist) > 10f)
						{
							value18 = ",penwidth=3";
						}
						textWriter2.WriteLine($"  \"{pred}\" -> \"{key3}\" [ style={value17}{value18} ];");
					}
				}
				new HashSet<Graph.Connection>();
				using (Dictionary<string, List<string>>.Enumerator enumerator17 = dictionary.GetEnumerator())
				{
					if (enumerator17.MoveNext())
					{
						KeyValuePair<string, List<string>> current23 = enumerator17.Current;
					}
				}
				foreach (KeyValuePair<string, string> item18 in rootComponents)
				{
					string key4 = item18.Key;
					string text8 = ((item18.Key == item18.Value) ? item18.Key : (item18.Key + " (" + item18.Value + ")"));
					text8 = text8 + " " + check.Records[key4].Dist;
					int num20 = g.Nodes[key4].From.Count((Graph.Edge edge15) => noteConnect(edge15, edge15.From));
					int num21 = g.Nodes[key4].To.Count((Graph.Edge edge15) => noteConnect(edge15, edge15.To));
					string text9 = null;
					if (num20 > 0 && num21 > 0)
					{
						text9 = "purple";
					}
					else if (num20 > 0)
					{
						text9 = "blue";
					}
					else if (num21 > 0)
					{
						text9 = "red";
					}
					if (g.Areas[key4].DefeatFlag > 0)
					{
						text9 = "lightpink";
					}
					string value19 = "";
					if (text9 != null)
					{
						value19 = ",style=filled,fillcolor=" + text9;
					}
					textWriter2.WriteLine($"    \"{item18.Key}\" [ shape=box,label=\"{escape2(text8)}\"{value19} ];");
				}
			}
			else
			{
				bool flag5 = !opt["showpart"];
				Dictionary<int, int> dictionary8 = (from c in check.Records.Values
					group c by (int)c.Dist).ToDictionary((IGrouping<int, GraphChecker.NodeRecord> g) => g.Key, (IGrouping<int, GraphChecker.NodeRecord> g) => g.Count());
				int num22 = dictionary8.Keys.Max();
				Dictionary<int, int> mainRank = new Dictionary<int, int> { [0] = 0 };
				int num23 = 0;
				int value20 = 1;
				for (int num24 = 1; num24 <= num22; num24++)
				{
					dictionary8.TryGetValue(num24, out var value21);
					if (value21 + num23 >= 5)
					{
						mainRank[num24] = num24;
						num23 = 0;
					}
					else if (num23 == 0)
					{
						value20 = num24;
						mainRank[num24] = num24;
						num23 += value21;
					}
					else
					{
						mainRank[num24] = value20;
						num23 += value21;
					}
				}
				foreach (KeyValuePair<int, List<Graph.Node>> item19 in new Dictionary<int, List<Graph.Node>> { [-1] = g.Nodes.Values.ToList() })
				{
					if (item19.Key >= 0)
					{
						textWriter2.WriteLine("subgraph {");
						textWriter2.WriteLine("   rank=same;");
					}
					foreach (Graph.Node item20 in item19.Value)
					{
						string area3 = item20.Area;
						if ((!flag5 || coreAreas.Contains(area3)) && (check.Records.ContainsKey(area3) || !g.Areas[area3].HasTag("optional")))
						{
							string value22 = (g.Areas[area3].HasTag("overworld") ? "box3d" : "box");
							string text10 = null;
							if (highlight.Contains(area3))
							{
								text10 = "lightblue";
							}
							else if (list34.Contains(area3))
							{
								text10 = "pink";
							}
							else if (!coreAreas.Contains(area3))
							{
								text10 = "lightgray";
							}
							string value23 = "";
							if (text10 != null)
							{
								value23 = ",style=filled,fillcolor=" + text10;
							}
							string text11 = area3;
							int num25 = item20.To.Count((Graph.Edge edge15) => edge15.To == null);
							if (num25 > 0)
							{
								text11 += $" ({num25})";
							}
							textWriter2.WriteLine($"    \"{item20.Area}\" [ shape={value22},label=\"{escape2(text11)}\"{value23} ];");
						}
					}
					if (item19.Key >= 0)
					{
						textWriter2.WriteLine("}");
					}
				}
				HashSet<Graph.Connection> hashSet2 = new HashSet<Graph.Connection>();
				HashSet<Graph.Edge> hashSet3 = new HashSet<Graph.Edge>();
				HashSet<Graph.Connection> hashSet4 = new HashSet<Graph.Connection>
				{
					new Graph.Connection("outskirts_sidetomb", "snowfield_hiddenpath_boss")
				};
				HashSet<Graph.Connection> hashSet5 = new HashSet<Graph.Connection>();
				HashSet<string> hashSet6 = new HashSet<string>();
				hashSet6.UnionWith(new string[8] { "chapel_boss", "outskirts_sidetomb", "outskirts_sidetomb_upper", "caelid_gaolcave_preboss", "caelid_gaolcave_boss", "graveyard", "caelid_caelem_boss", "limgrave_tower" });
				foreach (Graph.Node value37 in g.Nodes.Values)
				{
					foreach (Graph.Edge e2 in value37.To)
					{
						if (string.IsNullOrEmpty(e2.From) || string.IsNullOrEmpty(e2.To) || (flag5 && (!coreAreas.Contains(e2.From) || !coreAreas.Contains(e2.To))))
						{
							continue;
						}
						string text12 = "";
						Graph.Connection item = new Graph.Connection(e2.From, e2.To);
						if (flag4)
						{
							Graph.Edge edge14 = null;
							if (e2.Link.Pair != null && simpleLink2(e2) && simpleLink2(e2.Link.Pair))
							{
								edge14 = e2.Link.Pair;
							}
							else if (e2.IsWorld && simpleLink2(e2))
							{
								List<Graph.Edge> list35 = g.Nodes[e2.To].To.Where((Graph.Edge f) => f.To == e2.From && simpleLink2(f)).ToList();
								if (list35.Count == 1)
								{
									edge14 = list35[0];
								}
							}
							if (edge14 != null)
							{
								if (hashSet3.Contains(e2) && hashSet3.Contains(edge14))
								{
									continue;
								}
								hashSet3.Add(e2);
								hashSet3.Add(edge14);
								text12 = ",dir=both";
							}
						}
						else
						{
							if (hashSet2.Contains(item))
							{
								continue;
							}
							hashSet2.Add(item);
						}
						string value24 = (e2.IsFixed ? "dashed" : "solid");
						string o2 = "";
						string value25 = "";
						if (e2.LinkedExpr != null && e2.LinkedExpr.FreeVars().Any((string v) => g.ItemAreas.ContainsKey(v)))
						{
							value25 = "color=red";
						}
						string text13 = e2.From;
						string text14 = e2.To;
						float num26 = 0f;
						if (check.Records.ContainsKey(e2.To) && check.Records.ContainsKey(e2.From))
						{
							num26 = check.Records[e2.To].Dist - check.Records[e2.From].Dist;
						}
						if (num26 < 0f)
						{
							string text15 = text14;
							text14 = text13;
							text13 = text15;
							if (text12 == "")
							{
								text12 = ",dir=back";
							}
							num26 *= -1f;
						}
						string value26 = "";
						if ((hashSet6.Contains(e2.To) || hashSet6.Contains(e2.From) || (hashSet5.Contains(item) && e2.IsWorld)) && !hashSet4.Contains(item))
						{
							value25 = "color=blue";
							value26 = ",penwidth=3";
						}
						string value27 = $"  \"{text13}\" -{(flag4 ? ">" : "-")} \"{text14}\" [ style={value24},labelloc=t,label=\"{escape2(o2)}\"{value25}{text12}{value16}{value26} ];";
						textWriter2.WriteLine(value27);
					}
				}
			}
			textWriter2.WriteLine("}");
			textWriter2.Close();
		}
		void connectAllToClique(List<Graph.Edge> clique, List<Graph.Edge> cand)
		{
			Util.Shuffle(new Random(opt.Seed - rounds++), cand);
			Util.Shuffle(new Random(opt.Seed - rounds++), clique);
			if (cand.Intersect(nonEntrancePseudoCore).Any())
			{
				cand = cand.OrderBy((Graph.Edge item2) => (!nonEntrancePseudoCore.Contains(item2)) ? 1 : 0).ToList();
				if (nonEntrancePseudoCore.Contains(cand[clique.Count - 1]))
				{
					int num27 = cand.FindIndex((Graph.Edge item2) => !nonEntrancePseudoCore.Contains(item2));
					if (num27 >= 0)
					{
						List<Graph.Edge> list36 = cand;
						int index = clique.Count - 1;
						List<Graph.Edge> list37 = cand;
						int index2 = num27;
						Graph.Edge value28 = cand[num27];
						Graph.Edge value29 = cand[clique.Count - 1];
						list36[index] = value28;
						list37[index2] = value29;
					}
				}
				clique = clique.OrderBy((Graph.Edge edge15) => (!peripheryExits.Contains(edge15.Pair)) ? 1 : 0).ToList();
				List<int> unconditionalEntries = clique.Where((Graph.Edge edge15) => edge15.Expr == null).Select((Graph.Edge edge15, int i) => i).ToList();
				if (!cand.Where((Graph.Edge item2, int i) => !nonEntrancePseudoCore.Contains(item2) && unconditionalEntries.Contains(i)).Any() && unconditionalEntries.Count != 0)
				{
					int num28 = cand.FindIndex((Graph.Edge item2) => !nonEntrancePseudoCore.Contains(item2));
					int num29 = unconditionalEntries[0];
					if (num28 >= 0)
					{
						List<Graph.Edge> list36 = clique;
						int index2 = num29;
						List<Graph.Edge> list38 = clique;
						int index = num28;
						Graph.Edge value29 = clique[num28];
						Graph.Edge value28 = clique[num29];
						list36[index2] = value29;
						list38[index] = value28;
					}
				}
			}
			for (int num30 = 0; num30 < clique.Count; num30++)
			{
				Graph.Edge coreExit = cand[num30];
				Graph.Edge cliqueEntrance = clique[num30];
				connectToClique(coreExit, cliqueEntrance);
			}
		}
		void connectPairedPeriphery(string type)
		{
			tries = 0;
			while (true)
			{
				int num27 = tries;
				tries = num27 + 1;
				if (num27 >= 100)
				{
					break;
				}
				if (opt["explain"])
				{
					Console.WriteLine($"------------------------ Try {tries} ({type})");
				}
				check = checker.Check(opt, g, start, GraphChecker.CheckMode.Partial);
				unvisited = check.Unvisited.Intersect(pairedPeriphery).ToList();
				if (unvisited.Count == 0)
				{
					break;
				}
				if (opt["explainper"])
				{
					Console.WriteLine($"Unvisited: [{string.Join(",", unvisited)}] (missing items: [{string.Join(", ", check.UnvisitedItems)}])");
				}
				SwapUnreachableEdge(check, unvisited, tries, pairedOnly: true, CoreSelection.PeripheryOnly);
			}
			if (!opt["noconnectper"] && unvisited.Count > 0)
			{
				throw new Exception($"Couldn't solve seed {opt.DisplaySeed} - try a different one");
			}
			Console.WriteLine("");
		}
		void connectToClique(Graph.Edge coreExit, Graph.Edge cliqueEntrance)
		{
			if (opt["explainper"])
			{
				Console.WriteLine($"core {coreExit} ---> clique {cliqueEntrance}");
			}
			if (coreExit.Link == null)
			{
				outboundDetached.Remove(coreExit);
			}
			else
			{
				inboundDetached.Add(coreExit.Link);
				if (coreExit == coreExit.Link.Pair)
				{
					throw new Exception($"Case not handled: chosen core exit {coreExit} is a self-exit. Try a different seed.");
				}
				g.Disconnect(coreExit);
			}
			g.Connect(coreExit, cliqueEntrance);
			connectedToClique.Add(coreExit);
			nonEntrancePseudoCore.Remove(coreExit);
		}
		void crossVisit(Graph.Edge startEdge, Graph.Edge edge15, HashSet<string> hashSet7, List<Graph.Edge> coreEdges, bool flag7)
		{
			string to2 = edge15.To;
			bool flag6 = hashSet7.Contains(to2);
			hashSet7.Add(to2);
			if (expandedCore.Contains(to2))
			{
				coreEdges.Add(edge15);
				if (pseudoCore.ContainsKey(to2))
				{
					if (edge15.Link.Pair == null)
					{
						throw new Exception($"No pair for expanded core {to2} edge {edge15} -> {edge15.Link}");
					}
					nonEntrancePseudoCore.Add(edge15.Link.Pair);
				}
				if (startEdge != null && edge15 != startEdge)
				{
					peripheryExits.Add(edge15);
				}
			}
			else if (!flag6)
			{
				foreach (Graph.Edge item21 in g.Nodes[to2].To)
				{
					string to3 = item21.To;
					if (flag7)
					{
						Console.WriteLine($"  checking out {item21} from {to2}. to {to3} with world {item21.IsWorld}");
					}
					if (to3 != null && (item21.Pair != null || item21.IsWorld))
					{
						if (item21.LinkedExpr != null && item21.LinkedExpr.ToString() != to2)
						{
							startEdge = null;
						}
						crossVisit(startEdge, item21, hashSet7, coreEdges, flag7);
					}
				}
			}
		}
		bool dlcSide(AnnotationData.Side side)
		{
			return g.Areas[side.Area].Mode != g.ExcludeMode;
		}
		HashSet<string> domVisit(string text16)
		{
			if (dominators.TryGetValue(text16, out var value28))
			{
				return value28;
			}
			dominators[text16] = new HashSet<string> { text16 };
			if (rootPreds.TryGetValue(text16, out var value29))
			{
				value28 = null;
				foreach (string item22 in value29)
				{
					if (value28 == null)
					{
						value28 = new HashSet<string>(domVisit(item22));
					}
					else
					{
						value28.IntersectWith(domVisit(item22));
					}
				}
				if (value28 != null)
				{
					value28.Add(text16);
					dominators[text16] = value28;
				}
			}
			return dominators[text16];
		}
		static string escape(object obj)
		{
			if (obj == null)
			{
				return "";
			}
			return obj.ToString().Replace("\n", "\\l").Replace("\"", "\\\"") + "\\l";
		}
		static string escape2(object obj)
		{
			if (obj == null)
			{
				return "";
			}
			return obj.ToString().Replace("\n", "\\l").Replace("\"", "\\\"") + "\\l";
		}
		int getRanking(string text18, string text16)
		{
			if (!check.Records.TryGetValue(text16, out var _))
			{
				return maxRank - 6;
			}
			int num27 = -1;
			string text17 = getScc(text18);
			string toC = getScc(text16);
			check.Records.TryGetValue(text18, out var fromNode);
			HashSet<string> value29;
			if (text17 == toC)
			{
				num27 = maxRank - 1;
			}
			else if (dominators.TryGetValue(text17, out value29) && value29.Contains(toC))
			{
				num27 = maxRank - 2;
			}
			else if (fromNode != null && fromNode.Visited.Any((string v) => getScc(v) == toC))
			{
				num27 = maxRank - 3;
			}
			else if (fromNode != null)
			{
				List<Graph.Edge> ancestorPath = GetAncestorPath(check, text16, (string a) => fromNode.Visited.Contains(a), allowConds: false);
				if (ancestorPath != null)
				{
					int num28 = ancestorPath.Count((Graph.Edge edge15) => g.Areas[edge15.From].DefeatFlag > 0);
					num27 = maxRank - 10 - ancestorPath.Count - 10 * num28;
				}
			}
			if (num27 > 0)
			{
				return num27;
			}
			if (fromNode == null)
			{
				return -1000;
			}
			List<Graph.Edge> ancestorPath2 = GetAncestorPath(check, text16, (string a) => fromNode.Visited.Contains(a), allowConds: true);
			if (ancestorPath2 == null)
			{
				return -1000;
			}
			return -10 - ancestorPath2.Count((Graph.Edge edge15) => edge15.LinkedExpr != null || g.Areas[edge15.From].DefeatFlag > 0);
		}
		string getScc(string text16)
		{
			if (!check.Records.ContainsKey(text16))
			{
				return text16;
			}
			if (rootComponents.TryGetValue(text16, out var value28))
			{
				return value28;
			}
			List<Graph.Edge> ancestorPath = GetAncestorPath(check, text16, (string a) => rootComponents.ContainsKey(a), allowConds: true);
			if (ancestorPath == null)
			{
				throw new Exception("Internal exception: can't find connected component for area " + text16);
			}
			return rootComponents[text16] = ((ancestorPath.Count == 0) ? text16 : ancestorPath[0].From);
		}
		static bool isMinidungeonBoss(AnnotationData.Area area4)
		{
			if (area4.DefeatFlag > 0)
			{
				return area4.HasTag("minidungeon");
			}
			return false;
		}
		bool isTraversable(Graph.Edge edge15)
		{
			if (edge15.Link != null && edge15.LinkedExpr == null)
			{
				if (g.Areas[edge15.From].DefeatFlag > 0)
				{
					return false;
				}
				return true;
			}
			return false;
		}
		void makeFixed(Graph.Edge edge15)
		{
			g.EntranceIds[edge15.Name].IsFixed = true;
			edge15.IsFixed = true;
			edge15.Link.IsFixed = true;
		}
		bool noteConnect(Graph.Edge edge15, string text16)
		{
			return false;
		}
		static bool simpleLink(Graph.Edge edge15)
		{
			if (edge15.LinkedExpr != null && !(edge15.LinkedExpr.ToString() == edge15.From))
			{
				return edge15.LinkedExpr.ToString() == edge15.To;
			}
			return true;
		}
		static bool simpleLink2(Graph.Edge edge15)
		{
			if (edge15.LinkedExpr != null && !(edge15.LinkedExpr.ToString() == edge15.From))
			{
				return edge15.LinkedExpr.ToString() == edge15.To;
			}
			return true;
		}
		void strongAssign(string text16, string root)
		{
			if (!coreAreas.Contains(text16) || rootComponents[text16] != null)
			{
				return;
			}
			rootComponents[text16] = root;
			foreach (Graph.Edge item23 in g.Nodes[text16].From.Where(isTraversable))
			{
				strongAssign(item23.From, root);
			}
		}
		void strongVisit(string text16)
		{
			if (coreAreas.Contains(text16) && !rootComponents.ContainsKey(text16))
			{
				rootComponents[text16] = null;
				foreach (Graph.Edge item24 in g.Nodes[text16].To.Where(isTraversable))
				{
					strongVisit(item24.To);
				}
				ordering.Add(text16);
			}
		}
		void visitEdge(Graph.Edge edge15, List<Graph.Edge> visitedEdges)
		{
			if (!globalEdges.Add(edge15))
			{
				return;
			}
			visitedEdges.Add(edge15);
			foreach (Graph.Edge item25 in edgeEdges[edge15])
			{
				visitEdge(item25, visitedEdges);
			}
		}
	}

	public void PostProcess(GraphChecker.CheckRecord check, HashSet<string> coreAreas)
	{
		Dictionary<GameSpec.FromGame, List<string>> dictionary = new Dictionary<GameSpec.FromGame, List<string>>();
		dictionary[GameSpec.FromGame.DS1] = new List<string> { "parish_andre", "catacombs", "anorlondo_blacksmith" };
		dictionary[GameSpec.FromGame.DS3] = new List<string> { "firelink" };
		dictionary[GameSpec.FromGame.ER] = new List<string> { "roundtable", "limgrave", "liurnia" };
		dictionary.TryGetValue(opt.Game, out var value);
		List<GraphChecker.NodeRecord> list = (from area in value
			where check.Records.ContainsKey(area)
			select check.Records[area] into r
			orderby r.Visited.Count
			select r).ToList();
		GraphChecker.NodeRecord nodeRecord = null;
		if (list.Count > 0)
		{
			if (list[0].Visited.Count < 5)
			{
				nodeRecord = list[0];
			}
			else
			{
				HashSet<string> commonAreas = new HashSet<string>(g.Areas.Keys);
				foreach (GraphChecker.NodeRecord item4 in list)
				{
					commonAreas.IntersectWith(item4.Visited);
				}
				GraphChecker.NodeRecord nodeRecord2 = list.Find((GraphChecker.NodeRecord nodeRecord3) => commonAreas.IsSupersetOf(nodeRecord3.Visited.Where((string a) => g.Areas[a].IsBoss)));
				nodeRecord = ((nodeRecord2 == null) ? list[0] : nodeRecord2);
			}
		}
		List<string> list2 = nodeRecord?.Visited ?? new List<string>();
		if (!opt["skipprint"] && list2.Count > 0 && g.ExcludeMode != AnnotationData.AreaMode.Base)
		{
			Console.WriteLine("Areas before " + maybeName(nodeRecord.Area) + ": " + string.Join("; ", list2.OrderBy((string a) => check.Records[a].Dist).Select(maybeName)));
			Console.WriteLine("Other areas are not necessary to get there.");
			Console.WriteLine();
		}
		foreach (string item5 in value)
		{
			if (opt["explain"])
			{
				Console.WriteLine("Blacksmith " + item5 + ": " + string.Join(", ", check.Records[item5].Visited));
			}
		}
		Dictionary<GameSpec.FromGame, string> dictionary2 = new Dictionary<GameSpec.FromGame, string>
		{
			[GameSpec.FromGame.DS1] = "parish_church",
			[GameSpec.FromGame.DS3] = "settlement",
			[GameSpec.FromGame.ER] = "stormveil"
		};
		if (opt.Game == GameSpec.FromGame.ER)
		{
			g.AreaTiers = new Dictionary<string, int>();
			List<GraphChecker.NodeRecord> list3 = check.Records.Values.Where((GraphChecker.NodeRecord r) => g.IsMajorScalingBoss(g.Areas[r.Area])).ToList();
			g.AreaTiers["chapel_start"] = 1;
			g.AreaTiers["erdtree"] = 17;
			if (g.ExcludeMode == AnnotationData.AreaMode.Base)
			{
				g.AreaTiers["chapel_start"] = 21;
				g.AreaTiers["enirilim_radahn"] = 33;
			}
			List<int> list4;
			if (ann.Locations != null)
			{
				list4 = (from r in list3
					select ann.EnemyAreas[r.Area].ScalingTier into x
					orderby x
					select x).ToList();
			}
			else
			{
				list4 = new List<int>();
				for (int num = 0; num < list3.Count; num++)
				{
					float num2 = (float)num / (float)(list3.Count - 1);
					int item = (int)Math.Round(3f + num2 * 17f);
					list4.Add(item);
				}
			}
			int num3 = 0;
			foreach (GraphChecker.NodeRecord item6 in list3.OrderBy((GraphChecker.NodeRecord r) => r.Dist))
			{
				int value2 = list4[num3++];
				g.AreaTiers[item6.Area] = value2;
			}
			foreach (GraphChecker.NodeRecord item7 in check.Records.Values.OrderBy((GraphChecker.NodeRecord r) => r.Dist))
			{
				GraphChecker.NodeRecord rec = item7;
				if (!g.AreaTiers.TryGetValue(rec.Area, out var destTier) || g.Areas[rec.Area].IsExcluded)
				{
					continue;
				}
				List<Graph.Edge> ancestorPath = GetAncestorPath(check, rec.Area, earlierTier, allowConds: true);
				if (ancestorPath == null || ancestorPath.Count <= 1)
				{
					continue;
				}
				List<string> list5 = ancestorPath.Select((Graph.Edge e) => e.From).ToList();
				int num4 = g.AreaTiers[list5[0]];
				if (num4 + 1 >= destTier)
				{
					foreach (string item8 in list5)
					{
						g.AreaTiers[item8] = num4;
					}
				}
				else
				{
					int num5 = destTier - 1;
					List<float> source = list5.Select((string p) => check.Records[p].Dist).ToList();
					float num6 = source.Min();
					float num7 = source.Max();
					if (num6 == num7)
					{
						num7 = float.PositiveInfinity;
					}
					foreach (string item9 in list5)
					{
						float dist = check.Records[item9].Dist;
						float num8 = (dist - num6) / (num7 - num6);
						if (num8 < 0f || num8 > 1f)
						{
							throw new Exception($"Internal error: bad ratio math {num6} {dist} {num7}");
						}
						int value3 = (int)Math.Round((float)num4 + num8 * (float)(num5 - num4));
						g.AreaTiers[item9] = value3;
					}
				}
				list5.Add(rec.Area);
				bool earlierTier(string a)
				{
					if (a != rec.Area && g.AreaTiers.TryGetValue(a, out var value10))
					{
						return value10 <= destTier;
					}
					return false;
				}
			}
			foreach (GraphChecker.NodeRecord item10 in check.Records.Values.OrderBy((GraphChecker.NodeRecord r) => r.Dist))
			{
				if (g.AreaTiers.ContainsKey(item10.Area))
				{
					continue;
				}
				if (g.Areas[item10.Area].IsExcluded)
				{
					if (ann.EnemyAreas.TryGetValue(item10.Area, out var value4))
					{
						g.AreaTiers[item10.Area] = value4.ScalingTier;
					}
					continue;
				}
				List<Graph.Edge> ancestorPath2 = GetAncestorPath(check, item10.Area, (string a) => g.AreaTiers.ContainsKey(a), allowConds: true);
				if (ancestorPath2 == null || ancestorPath2.Count == 0)
				{
					throw new Exception("Internal error: couldn't find ancestor of " + item10.Area + " with core path scaling");
				}
				_ = g.AreaTiers[ancestorPath2[0].From];
				List<string> list6 = ancestorPath2.Select((Graph.Edge e) => e.To).ToList();
				string key = item10.Visited.Where((string a) => g.AreaTiers.ContainsKey(a)).MaxBy((string a) => check.Records[a].Dist);
				int value5 = g.AreaTiers[key];
				foreach (string item11 in list6)
				{
					if (!g.AreaTiers.ContainsKey(item11))
					{
						g.AreaTiers[item11] = value5;
					}
				}
			}
			foreach (AnnotationData.Area value12 in g.Areas.Values)
			{
				if (g.AreaTiers.ContainsKey(value12.Name))
				{
					continue;
				}
				if (value12.OpenArea != null && g.AreaTiers.TryGetValue(value12.OpenArea, out var value6))
				{
					g.AreaTiers[value12.Name] = value6;
				}
				else if (check.Unvisited.Count == 0)
				{
					if (!value12.IsExcluded)
					{
						throw new Exception("No tier for optional area " + value12.Name);
					}
					if (ann.EnemyAreas.TryGetValue(value12.Name, out var value7))
					{
						g.AreaTiers[value12.Name] = value7.ScalingTier;
					}
				}
			}
			foreach (string item12 in list2)
			{
				if (g.Areas[item12].IsBoss)
				{
					g.AreaTiers[item12] = 1;
				}
			}
			if (opt["crawl"])
			{
				foreach (AnnotationData.Area value13 in g.Areas.Values)
				{
					if (!value13.IsCore)
					{
						g.AreaTiers.Remove(value13.Name);
					}
				}
			}
		}
		else
		{
			g.AreaRatios = new Dictionary<string, (float, float)>();
			int num9 = 0;
			foreach (GraphChecker.NodeRecord item13 in check.Records.Values.OrderBy((GraphChecker.NodeRecord r) => r.Dist))
			{
				float num10 = 1f;
				if (!g.Areas[item13.Area].HasTag("optional"))
				{
					num9++;
				}
				bool isBoss = g.Areas[item13.Area].IsBoss;
				if (list2.Contains(item13.Area) && isBoss && (double)num10 > 0.05)
				{
					num10 = 0.05f;
				}
				float num11 = 1f;
				float item2 = 1f;
				if (!g.Areas[item13.Area].HasTag("end") && ann.DefaultCost.TryGetValue(item13.Area, out var value8))
				{
					num11 = getRatioMeasure(num10, ann.HealthScaling) / getRatioMeasure(value8, ann.HealthScaling);
					float value9;
					if (num11 < 1f && (double)num9 / (double)check.Records.Count > 0.7)
					{
						num11 = 1f;
					}
					else if ((double)value8 <= (ann.DefaultCost.TryGetValue(dictionary2[opt.Game], out value9) ? ((double)value9) : 0.25) && num11 < 1f)
					{
						num11 = 1f;
					}
					else
					{
						item2 = getRatioMeasure(num10, ann.DamageScaling) / getRatioMeasure(value8, ann.DamageScaling);
					}
				}
				g.AreaRatios[item13.Area] = (num11, item2);
			}
		}
		Dictionary<GameSpec.FromGame, HashSet<string>> criticalArea = new Dictionary<GameSpec.FromGame, HashSet<string>>
		{
			[GameSpec.FromGame.DS1] = new HashSet<string> { "anorlondo_os" },
			[GameSpec.FromGame.DS3] = new HashSet<string> { "firelink" },
			[GameSpec.FromGame.ER] = new HashSet<string> { "farumazula_maliketh", "leyndell_erdtree" }
		};
		Dictionary<string, int> vanillaTiers;
		if (!opt["skipprint"])
		{
			Console.WriteLine("This spoiler log lists all accessible areas in order from earliest to latest.");
			Console.WriteLine("For each area, it lists ways to enter the area, from earliest to latest.");
			Console.WriteLine("Paired warps are only listed once, under the entrance list of whichever area is later.");
			Console.WriteLine();
			Console.WriteLine("How to get to an area:");
			Console.WriteLine("- Find the area name in the list below.");
			Console.WriteLine("- The first indented line below the area name will show the earliest available way to enter it.");
			Console.WriteLine("- Repeat this process, going backwards in the list until you find an area you've already visited.");
			Console.WriteLine();
			Console.WriteLine("If you're stuck in general, a good strategy is to find the first major boss arena which");
			Console.WriteLine("you haven't visited yet and see how to get there. Bosses are marked with <<<<<");
			Console.WriteLine();
			if (opt.Game == GameSpec.FromGame.ER)
			{
				vanillaTiers = (ann.Locations?.EnemyAreas ?? new List<AnnotationData.EnemyLocArea>()).ToDictionary((AnnotationData.EnemyLocArea a) => a.Name, (AnnotationData.EnemyLocArea a) => a.ScalingTier);
				foreach (GraphChecker.NodeRecord item14 in check.Records.Values.OrderBy((GraphChecker.NodeRecord r) => r.Dist))
				{
					if (coreAreas.Contains(item14.Area))
					{
						printArea(item14, getTier(item14.Area));
					}
				}
				if (!opt["skipper"])
				{
					Console.WriteLine();
					Console.WriteLine();
					Console.WriteLine("Optional areas:");
					bool flag = false;
					foreach (GraphChecker.NodeRecord item15 in check.Records.Values.OrderBy((GraphChecker.NodeRecord r) => r.Dist))
					{
						if (!coreAreas.Contains(item15.Area))
						{
							printArea(item15, getTier(item15.Area));
							flag = true;
						}
					}
					if (!flag)
					{
						Console.WriteLine("(none)");
					}
				}
				Console.WriteLine();
				Console.WriteLine();
			}
			else
			{
				foreach (GraphChecker.NodeRecord item16 in check.Records.Values.OrderBy((GraphChecker.NodeRecord r) => r.Dist))
				{
					float item3 = g.AreaRatios[item16.Area].Item1;
					printArea(item16, $"{item3 * 100f:0.}%");
				}
			}
		}
		Console.WriteLine($"Finished {opt.DisplaySeed}");
		if (opt["explain"] && nodeRecord != null)
		{
			Console.WriteLine("Pre-Blacksmith areas (" + nodeRecord.Area + "): " + string.Join(", ", list2));
		}
		if (!opt["dumpdist"])
		{
			return;
		}
		Dictionary<GameSpec.FromGame, int> dictionary3 = new Dictionary<GameSpec.FromGame, int>
		{
			[GameSpec.FromGame.DS1] = 60,
			[GameSpec.FromGame.DS3] = 70,
			[GameSpec.FromGame.ER] = 100
		};
		float max = (from r in check.Records.Values
			where !r.Area.StartsWith("kiln")
			select r.Dist).Max();
		foreach (KeyValuePair<string, float> item17 in check.Records.Values.OrderBy((GraphChecker.NodeRecord r) => r.Dist).ToDictionary((GraphChecker.NodeRecord r) => r.Area, (GraphChecker.NodeRecord r) => getAreaCost(r.Dist)))
		{
			if (!g.Areas[item17.Key].HasTag("optional"))
			{
				Console.WriteLine($"{item17.Key}: {item17.Value}  # SL {(int)(10f + (float)dictionary3[opt.Game] * item17.Value)}");
			}
		}
		float getAreaCost(float num12)
		{
			return Math.Min(num12 / max, 1f);
		}
		static float getRatioMeasure(float cost, float maxRatio)
		{
			return 1f + (maxRatio - 1f) * cost;
		}
		string getTier(string area)
		{
			if (g.AreaTiers.TryGetValue(area, out var value10))
			{
				if (vanillaTiers.TryGetValue(area, out var value11))
				{
					return $"tier {value10}, previously {value11}";
				}
				return $"tier {value10}";
			}
			return null;
		}
		string maybeName(string area)
		{
			if (!g.Areas.TryGetValue(area, out var value10))
			{
				return area;
			}
			return value10.Text ?? area;
		}
		void printArea(GraphChecker.NodeRecord nodeRecord3, string scaleAmount)
		{
			if (!opt["showexcluded"] && g.Areas[nodeRecord3.Area].IsExcluded)
			{
				return;
			}
			if (criticalArea[opt.Game].Contains(nodeRecord3.Area))
			{
				Console.WriteLine(">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");
			}
			bool isBoss2 = g.Areas[nodeRecord3.Area].IsBoss;
			string text = (opt["debugareas"] ? (" [" + string.Join(",", new SortedSet<string>(nodeRecord3.Visited)) + "]") : "");
			string text2 = ((opt["scale"] && scaleAmount != null) ? (" (scaling: " + scaleAmount + ")") : "");
			Console.WriteLine(maybeName(nodeRecord3.Area) + text2 + text + (isBoss2 ? " <<<<<" : ""));
			foreach (KeyValuePair<Graph.Edge, float> item18 in nodeRecord3.InEdge.OrderBy((KeyValuePair<Graph.Edge, float> e) => e.Value))
			{
				Graph.Edge key2 = item18.Key;
				List<string> list7 = new List<string>();
				if (key2.LinkedExpr != null)
				{
					list7.AddRange(key2.LinkedExpr.Substitute(g.ConfigExprs).FreeVars().SelectMany((string a) => (!g.ItemAreas.TryGetValue(a, out var value11)) ? new List<string>() : value11)
						.Distinct());
				}
				string value10 = ((list7.Count == 0) ? "" : (", using " + ((list7.Count == 1) ? "an item" : "items") + " from " + string.Join("; ", list7.Select((string a) => maybeName(a)))));
				if (key2.Text == key2.Link.Text || key2.IsFixed)
				{
					Console.WriteLine($"  Preexisting: {maybeName(key2.From)} --> {maybeName(nodeRecord3.Area)} ({key2.Text}){value10}");
				}
				else
				{
					Console.WriteLine($"  Random: {maybeName(key2.From)} ({key2.Text}) --> {maybeName(nodeRecord3.Area)} ({key2.Link.Text}){value10}");
				}
			}
		}
	}

	private List<Graph.Edge> GetAncestorPath(GraphChecker.CheckRecord check, string start, Predicate<string> isRoot, bool allowConds)
	{
		if (!check.Records.ContainsKey(start))
		{
			return null;
		}
		LinkedList<string> linkedList = new LinkedList<string>();
		linkedList.AddLast(start);
		Dictionary<string, (Graph.Edge, string)> dictionary = new Dictionary<string, (Graph.Edge, string)>();
		while (linkedList.Count > 0)
		{
			string text = linkedList.First();
			linkedList.RemoveFirst();
			if (isRoot(text))
			{
				List<Graph.Edge> list = new List<Graph.Edge>();
				while (dictionary.ContainsKey(text) && text != start)
				{
					Graph.Edge item;
					(item, text) = dictionary[text];
					if (list.Contains(item))
					{
						break;
					}
					list.Add(item);
				}
				return list;
			}
			foreach (Graph.Edge key in check.Records[text].InEdge.Keys)
			{
				string text2 = key.From;
				if ((allowConds || key.LinkedExpr == null || !(key.LinkedExpr.ToString() != text2)) && !dictionary.ContainsKey(text2))
				{
					dictionary[text2] = (key, text);
					linkedList.AddLast(text2);
				}
			}
		}
		return null;
	}

	private void ConnectEdges(List<Graph.Edge> allTos, List<Graph.Edge> allFroms, string desc)
	{
		if (desc != null)
		{
			desc += " ";
		}
		foreach (EdgeSilo edgeType in Enum.GetValues(typeof(EdgeSilo)))
		{
			List<Graph.Edge> list = allTos.Where((Graph.Edge e) => e.Pair == null == (edgeType == EdgeSilo.UNPAIRED)).ToList();
			List<Graph.Edge> list2 = allFroms.Where((Graph.Edge e) => e.Pair == null == (edgeType == EdgeSilo.UNPAIRED)).ToList();
			if (list.Count == 0 && list2.Count == 0)
			{
				continue;
			}
			Console.WriteLine($"Connecting {edgeType.ToString().ToLowerInvariant()} {desc}edges: {list2.Count} outgoing, {list.Count} incoming");
			while (true)
			{
				Graph.Edge edge = null;
				int num = 0;
				if (num < list.Count)
				{
					edge = list[num];
					if (edge.From != null)
					{
						throw new Exception($"Connected edge still left: {edge}");
					}
					list.RemoveAt(num);
					list2.Remove(edge.Pair);
				}
				if (edge == null)
				{
					break;
				}
				bool flag = g.Areas[edge.To].HasTag("overworld") || g.Areas[edge.To].HasTag("overworld_adjacent");
				Graph.Edge edge2 = null;
				if (list2.Count == 0)
				{
					if (edge.Pair == null)
					{
						throw new Exception("Ran out of eligible edges");
					}
					edge2 = edge.Pair;
				}
				else
				{
					int num2 = -1;
					for (int num3 = 0; num3 < list2.Count; num3++)
					{
						Graph.Edge edge3 = list2[num3];
						if (edge3.To != null)
						{
							throw new Exception($"Connected edge still left: {edge3}");
						}
						if (edge.Pair != edge3 && edge.Pair == null == (edge3.Pair == null))
						{
							bool flag2 = g.Areas[edge3.From].HasTag("overworld") || g.Areas[edge3.From].HasTag("overworld_adjacent");
							if (!(flag && flag2))
							{
								num2 = num3;
								break;
							}
							if (num2 == -1)
							{
								num2 = num3;
							}
						}
					}
					if (num2 == -1)
					{
						break;
					}
					edge2 = list2[num2];
					list2.RemoveAt(num2);
					list.Remove(edge2.Pair);
				}
				if (edge2 == null)
				{
					break;
				}
				if (edge.IsFixed || edge2.IsFixed)
				{
					throw new Exception($"Internal error: found fixed edges in randomization {edge} ({edge.IsFixed}) and {edge2} ({edge2.IsFixed})");
				}
				if (opt["explain"] || opt["dumpedges"])
				{
					Console.WriteLine($"{edge2} -> {edge} [{list2.Count}, {list.Count}]");
				}
				g.Connect(edge2, edge);
			}
			if (list.Count > 0 || list2.Count > 0)
			{
				throw new Exception("Internal error: unconnected edges after randomization:\nFrom edges: " + string.Join(", ", list) + "\nTo edges: " + string.Join(", ", list2));
			}
		}
	}

	private bool SwapUnreachableEdge(GraphChecker.CheckRecord check, List<string> unvisited, int tries, bool pairedOnly, CoreSelection coreSelection, HashSet<string> tried = null)
	{
		Graph.Edge unreachedEdge = null;
		Util.Shuffle(new Random(opt.Seed + tries), unvisited);
		if (tries > 100 && tried != null)
		{
			unvisited = unvisited.OrderBy((string a) => tried.Contains(a) ? 1 : 0).ToList();
		}
		bool flag = check.Records.Count((KeyValuePair<string, GraphChecker.NodeRecord> r) => !g.Areas[r.Key].IsExcluded) <= 4;
		List<string> list = new List<string>();
		foreach (string item in unvisited)
		{
			foreach (Graph.Edge item2 in g.Nodes[item].From)
			{
				if (!isEdgeEligible(item2) || item2.LinkedExpr == null)
				{
					continue;
				}
				AnnotationData.Expr expr = item2.LinkedExpr.Substitute(g.ConfigExprs).Simplify();
				bool flag2 = false;
				List<string> list2 = new List<string>();
				foreach (string item3 in expr.FreeVars())
				{
					List<string> value;
					if (g.Areas.ContainsKey(item3))
					{
						if (item3 == item2.From || item3 == item2.To)
						{
							flag2 = true;
						}
						list2.Add(item3);
					}
					else if (g.ItemAreas.TryGetValue(item3, out value))
					{
						list2.AddRange(value);
					}
				}
				list2 = list2.Intersect(unvisited).ToList();
				if (!(list2.Count == 0 || flag2))
				{
					list.AddRange(list2);
				}
			}
		}
		if (list.Count > 0)
		{
			unvisited = list.Distinct().Concat(unvisited.Except(list)).ToList();
		}
		bool flag3 = false;
		foreach (string item4 in unvisited)
		{
			foreach (Graph.Edge item5 in g.Nodes[item4].From)
			{
				if ((!flag || !sideTag(item5, "avoidstart")) && !sidePairTag(item5, "fakegaol") && isEdgeEligible(item5))
				{
					if (item5.LinkedExpr == null)
					{
						unreachedEdge = item5;
						flag3 = true;
						break;
					}
					if (unreachedEdge == null)
					{
						unreachedEdge = item5;
					}
				}
			}
			if (unreachedEdge != null && flag3)
			{
				break;
			}
		}
		if (unreachedEdge == null)
		{
			if (pairedOnly && (opt["warp"] || opt.Game == GameSpec.FromGame.ER))
			{
				return false;
			}
			throw new Exception($"Could not find edge into unreachable areas [{string.Join(", ", unvisited)}] starting from {g.Start.Area} (missing items: [{string.Join(", ", check.UnvisitedItems)}])");
		}
		(Graph.Edge, float) tuple = (null, 0f);
		Graph.Edge edge = null;
		int num = 0;
		foreach (GraphChecker.NodeRecord item6 in check.Records.Values.OrderBy((GraphChecker.NodeRecord r) => r.Dist))
		{
			if (opt["explain"])
			{
				Console.WriteLine($"{item6.Area}: {item6.Dist}");
				foreach (KeyValuePair<Graph.Edge, float> item7 in item6.InEdge.OrderBy((KeyValuePair<Graph.Edge, float> e) => e.Value))
				{
					Graph.Edge key = item7.Key;
					Console.WriteLine($"  From {key.From}{(key.IsFixed ? " (world)" : "")}: {item7.Value}");
				}
			}
			KeyValuePair<Graph.Edge, float> keyValuePair = (from e in item6.InEdge
				orderby e.Value
				where isEdgeEligible(e.Key) && areEdgesCompatible(e.Key, unreachedEdge)
				select e).LastOrDefault();
			if (keyValuePair.Key == null)
			{
				continue;
			}
			int count = g.Nodes[item6.Area].From.Count;
			if (count > num)
			{
				edge = keyValuePair.Key;
				num = count;
			}
			KeyValuePair<Graph.Edge, float> keyValuePair2 = item6.InEdge.OrderBy((KeyValuePair<Graph.Edge, float> e) => e.Value).First();
			if (keyValuePair2.Key != keyValuePair.Key)
			{
				if (opt["explain"])
				{
					Console.WriteLine($"  Min {keyValuePair2.Value}, Max editable {keyValuePair.Value} in {keyValuePair.Key}");
				}
				if (keyValuePair.Value >= tuple.Item2)
				{
					tuple = (keyValuePair.Key, keyValuePair.Value);
				}
			}
		}
		var (edge2, _) = tuple;
		if (edge2 == null)
		{
			if (edge != null)
			{
				if (opt["explain"])
				{
					Console.WriteLine("!!!!!!!!!!! Picking non-redundant edge, but last reachable");
				}
				edge2 = edge;
			}
			else
			{
				edge2 = (from e in check.Records.Keys.SelectMany((string a) => g.Nodes[a].To)
					where isEdgeEligible(e) && areEdgesCompatible(e, unreachedEdge)
					select e).LastOrDefault();
				if (opt["explain"])
				{
					Console.WriteLine($"!!!!!!!!!!! Picking any edge whatsoever to {unreachedEdge}");
					if (opt["explainedge"])
					{
						foreach (Graph.Edge item8 in check.Records.Keys.SelectMany((string a) => g.Nodes[a].To))
						{
							Console.WriteLine($"{item8} - eligible {isEdgeEligible(item8)}, compatible {areEdgesCompatible(item8, unreachedEdge)}");
						}
					}
				}
				if (edge2 == null)
				{
					throw new Exception("No swappable edge found to inaccessible areas. This can happen a lot with low # of randomized entrances.");
				}
			}
		}
		if (opt["explain"] || opt["dumpedges"])
		{
			Console.WriteLine($"Swap unreached: {unreachedEdge}");
			Console.WriteLine($"Swap redundant: {edge2}");
			Console.WriteLine("Candidates: " + string.Join(", ", unvisited.Take(7)));
		}
		if (tried != null)
		{
			tried.Add(unreachedEdge.To);
		}
		g.SwapConnectedEdges(edge2, unreachedEdge);
		return !opt["unconnected"];
		bool areEdgesCompatible(Graph.Edge edge3, Graph.Edge found)
		{
			if (sideTag(edge3, "start") && sideTag(found, "avoidstart"))
			{
				return false;
			}
			if (sidePairTag(edge3, "fakegaol"))
			{
				return false;
			}
			if (coreSelection != CoreSelection.PeripheryOnly)
			{
				return true;
			}
			if (found.Side.IsPseudoCore || found.Link.Side.IsPseudoCore)
			{
				if (!g.Areas[edge3.From].IsCore)
				{
					return !g.Areas[edge3.To].IsCore;
				}
				return false;
			}
			return true;
		}
		bool isEdgeEligible(Graph.Edge edge3)
		{
			if (!edge3.IsFixed && edge3.Pair != null == pairedOnly)
			{
				if (coreSelection == CoreSelection.CoreOnly && !edge3.Side.IsCore)
				{
					return false;
				}
				if (coreSelection == CoreSelection.PeripheryOnly && edge3.Side.IsCore)
				{
					return false;
				}
				return true;
			}
			return false;
		}
		static bool sidePairTag(Graph.Edge e, string tag)
		{
			if (!sideTag(e, tag))
			{
				if (e.Pair != null)
				{
					return sideTag(e.Pair, tag);
				}
				return false;
			}
			return true;
		}
		static bool sideTag(Graph.Edge e, string tag)
		{
			if (!e.Side.HasTag(tag))
			{
				if (e.Link != null)
				{
					return e.Link.Side.HasTag(tag);
				}
				return false;
			}
			return true;
		}
	}

	private bool MoveAreaEarlier(GraphChecker.CheckRecord check, List<string> triedSwaps)
	{
		bool didSwap = false;
		List<string> list = (from r in check.Records.Values
			orderby r.Dist
			select r.Area).ToList();
		if (opt["explain"])
		{
			Console.WriteLine("Trying to place Firelink now. Overall order: [" + string.Join(",", list.Select((string a, int i) => $"{a}:{i}")) + "]");
		}
		Dictionary<string, int> areaIndex = list.Select((string a, int i) => (a: a, i: i)).ToDictionary(((string a, int i) a) => a.a, ((string a, int i) a) => a.i);
		int num = list.Count((string a) => !g.Areas[a].HasTag("trivial"));
		string text = list.Where((string a) => !g.Areas[a].HasTag("trivial")).Skip(num * 15 / 100).FirstOrDefault();
		int reasonableIndex = ((text == null) ? list.Count : list.IndexOf(text));
		if (opt["explain"])
		{
			Console.WriteLine($"Last reasonable area for Firelink requisites: {text}. Total count {list.Where((string a) => !g.Areas[a].HasTag("trivial")).Count()}");
		}
		Dictionary<string, int> randomIn = new Dictionary<string, int>();
		Dictionary<int, List<string>> byRandomIn = new Dictionary<int, List<string>>();
		foreach (string item in list)
		{
			int num2 = g.Nodes[item].From.Count((Graph.Edge e) => !e.IsFixed && (opt["unconnected"] || e.Pair != null));
			randomIn[item] = num2;
			Util.AddMulti(byRandomIn, num2, item);
		}
		if (opt["latewarp"] || opt["instawarp"])
		{
			tryPlace("firelink", reasonableOnly: true);
		}
		else
		{
			bool flag = tryPlace("firelink", reasonableOnly: true);
			List<string> accessibleAreas = new List<string> { "firelink_cemetery" };
			if (flag)
			{
				accessibleAreas.Add("firelink");
			}
			List<string> list2 = new List<string> { "coiledsword" };
			List<string> list3 = new List<string>();
			List<string> list4 = new List<string>();
			bool flag2;
			do
			{
				foreach (string item2 in list2)
				{
					if (!list3.Contains(item2))
					{
						list3.Add(item2);
						list4.AddRange(g.ItemAreas[item2].Except(list4));
					}
				}
				flag2 = false;
				foreach (string item3 in list4.ToList())
				{
					_ = g.Nodes[item3];
					if (randomIn[item3] > 0)
					{
						continue;
					}
					Dictionary<string, List<string>> fixedIn = getFixedIn(item3);
					if (fixedIn.Count == 0)
					{
						continue;
					}
					string text2 = fixedIn.Keys.OrderBy((string a) => fixedIn[a].Count).First();
					if (!list4.Contains(text2) && !accessibleAreas.Contains(text2))
					{
						list4.Add(text2);
						flag2 = true;
					}
					foreach (string item4 in fixedIn[text2])
					{
						if (g.ItemAreas.ContainsKey(item4) && !list2.Contains(item4))
						{
							list2.Add(item4);
							flag2 = true;
						}
						else if (g.Nodes.ContainsKey(item4) && !list4.Contains(item4))
						{
							list4.Add(item4);
							flag2 = true;
						}
					}
				}
				if (opt["explain"])
				{
					Console.WriteLine($"At end of iteration, have items {string.Join(",", list2)} and areas {string.Join(",", list4)}, with adjustable {string.Join(",", list4.Where((string a) => !accessibleAreas.Contains(a) && randomIn[a] > 0))}");
				}
			}
			while (flag2);
			List<string> list5 = list4.Where((string a) => !accessibleAreas.Contains(a) && randomIn[a] > 0).ToList();
			if (!flag)
			{
				list5.Insert(0, "firelink");
			}
			foreach (string item5 in list5)
			{
				tryPlace(item5, reasonableOnly: false, accessibleAreas);
				accessibleAreas.Add(item5);
			}
		}
		return didSwap;
		Dictionary<string, List<string>> getFixedIn(string area)
		{
			Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>();
			foreach (Graph.Edge item6 in g.Nodes[area].From.Where((Graph.Edge e) => e.IsFixed))
			{
				List<string> value = ((item6.LinkedExpr == null) ? new List<string>() : item6.LinkedExpr.FreeVars().ToList());
				dictionary[item6.From] = value;
			}
			return dictionary;
		}
		bool tryPlace(string subst, bool reasonableOnly, List<string> root = null)
		{
			if (areaIndex[subst] <= reasonableIndex)
			{
				return true;
			}
			List<string> list6 = byRandomIn[randomIn[subst]].ToList();
			list6.Remove(subst);
			if (root != null)
			{
				list6.RemoveAll((string c) => root.Contains(c) && areaIndex[c] < areaIndex[subst]);
			}
			if (opt["explain"])
			{
				Console.WriteLine($"Candidates for {subst} ({areaIndex[subst]}): {string.Join(",", list6.Select((string c) => $"{c}:{areaIndex[c]}"))}");
			}
			list6.RemoveAll((string c) => triedSwaps.Contains(string.Join(",", new SortedSet<string> { subst, c })));
			if (opt["explain"])
			{
				Console.WriteLine("Candidates for " + subst + " without tried: " + string.Join(",", list6));
			}
			list6.RemoveAll((string area) => check.Records[area].InEdge.All((KeyValuePair<Graph.Edge, float> e) => e.Key.IsFixed));
			if (opt["explain"])
			{
				Console.WriteLine("Candidates for " + subst + " with out edge: " + string.Join(",", list6));
			}
			if (list6.Count == 0)
			{
				return false;
			}
			List<string> list7 = list6.Where((string c) => areaIndex[c] <= reasonableIndex).ToList();
			if (list7.Count == 0 && reasonableOnly)
			{
				return false;
			}
			string text3 = ((list7.Count > 1 && areaIndex[list6[0]] <= 1) ? list6[1] : list6[0]);
			if (opt["explain"])
			{
				Console.WriteLine("Final choice: " + text3);
			}
			g.SwapConnectedAreas(subst, text3);
			triedSwaps.Add(string.Join(",", new SortedSet<string> { subst, text3 }));
			didSwap = true;
			return true;
		}
	}
}
