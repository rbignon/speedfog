using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsIds;
using YamlDotNet.Serialization;

namespace FogMod;

public class Randomizer
{
	public ItemReader.Result Randomize(RandomizerOptions opt, GameSpec.FromGame game, string gameDir, string outDir, MergedMods mergedMods, Action<string> notify = null)
	{
		opt.InitFeatures();
		if (game == GameSpec.FromGame.ER)
		{
			opt[Feature.NoBossBonfire] = opt["bossrush"];
			opt[Feature.AllBossBonfire] = false;
			opt[Feature.AllowUnlinked] = opt["bossrush"] || opt["endless"] || opt["crawl"];
			opt[Feature.ForceUnlinked] = opt["bossrush"] || opt["endless"] || opt["crawl"];
			opt[Feature.Segmented] = opt["bossrush"] || opt["endless"];
			opt[Feature.SegmentFortresses] = opt["endless"] || opt["crawl"];
			opt[Feature.NoKeyItemsRequired] = opt["bossrush"] || opt["endless"];
			opt[Feature.RemoveRewards] = opt["bossrush"];
			opt[Feature.AlwaysEnableBosses] = opt["bossrush"] || opt["endless"];
			opt[Feature.AddOverworldStakes] = opt["bossrush"] || opt["endless"];
			opt[Feature.RestrictBossArenas] = opt["bossrush"];
			opt[Feature.ChapelInit] = opt["bossrush"] || opt["endless"];
			opt[Feature.StartFromWretch] = opt["bossrush"];
			opt[Feature.BonfireShop] = opt["bossrush"] || opt["endless"];
			opt[Feature.EldenCoin] = opt["bossrush"];
			opt[Feature.StormhillLiurniaWall] = opt["endless"] || opt["crawl"];
		}
		if (game == GameSpec.FromGame.ER && (opt["basic"] || opt["configgen"]))
		{
			Events events = new Events("eldendata\\Base\\er-common.emedf.json", darkScriptMode: true, paramAwareMode: true);
			if (!opt["configgen"])
			{
				EventConfig eventConfig;
				using (StreamReader input = File.OpenText("eldendata\\Base\\fogevents.txt"))
				{
					eventConfig = new DeserializerBuilder().Build().Deserialize<EventConfig>(input);
					eventConfig.MakeWarpCommands(events);
				}
				new GameDataWriterE().Write(opt, null, null, null, null, events, eventConfig, null);
			}
			return null;
		}
		notify?.Invoke("Randomizing");
		if (mergedMods == null)
		{
			mergedMods = new MergedMods(gameDir);
		}
		if (!opt["dump"])
		{
			Console.WriteLine($"Options and seed: {opt}");
		}
		object path;
		switch (game)
		{
		default:
			path = "eldendata\\Base\\fog.txt";
			break;
		case GameSpec.FromGame.DS1:
		case GameSpec.FromGame.DS1R:
			path = "dist\\fog.txt";
			break;
		case GameSpec.FromGame.DS3:
			path = "fogdist\\fog.txt";
			break;
		}
		IDeserializer deserializer = new DeserializerBuilder().Build();
		AnnotationData annotationData;
		using (StreamReader input2 = File.OpenText((string)path))
		{
			annotationData = deserializer.Deserialize<AnnotationData>(input2);
		}
		annotationData.SetGame(game);
		Events events2 = null;
		switch (game)
		{
		case GameSpec.FromGame.DS3:
		{
			using (StreamReader input5 = File.OpenText("fogdist\\locations.txt"))
			{
				annotationData.Locations = deserializer.Deserialize<AnnotationData.FogLocations>(input5);
			}
			events2 = new Events("fogdist\\Base\\ds3-common.emedf.json");
			break;
		}
		case GameSpec.FromGame.ER:
		{
			events2 = new Events("eldendata\\Base\\er-common.emedf.json", darkScriptMode: true, paramAwareMode: true);
			annotationData.DefaultCost = new Dictionary<string, float>();
			annotationData.Locations = new AnnotationData.FogLocations();
			using (StreamReader input3 = File.OpenText("eldendata\\Base\\foglocations.txt"))
			{
				AnnotationData.FogLocations fogLocations = deserializer.Deserialize<AnnotationData.FogLocations>(input3);
				annotationData.Locations.Items = fogLocations.Items;
			}
			using (StreamReader input4 = File.OpenText("eldendata\\Base\\foglocations2.txt"))
			{
				AnnotationData.FogLocations fogLocations2 = deserializer.Deserialize<AnnotationData.FogLocations>(input4);
				annotationData.Locations.EnemyAreas = fogLocations2.EnemyAreas;
				annotationData.Locations.Enemies = fogLocations2.Enemies;
				annotationData.EnemyAreas = annotationData.Locations.EnemyAreas.ToDictionary((AnnotationData.EnemyLocArea a) => a.Name, (AnnotationData.EnemyLocArea a) => a);
			}
			break;
		}
		}
		Graph graph = new Graph();
		graph.Construct(opt, annotationData);
		ItemReader.Result result = new ItemReader().FindItems(opt, annotationData, graph, events2, gameDir, mergedMods);
		if (!opt["dump"])
		{
			Console.WriteLine(result.Randomized ? ("Key item hash: " + result.ItemHash) : "No key items randomized");
			Console.WriteLine();
		}
		if (mergedMods.Count > 0)
		{
			Console.WriteLine("Mod directories to merge:");
			foreach (string dir in mergedMods.Dirs)
			{
				Console.WriteLine(dir);
			}
			Console.WriteLine();
		}
		new GraphConnector(opt, graph, annotationData).Connect();
		if (opt["bonedryrun"])
		{
			return result;
		}
		Console.WriteLine();
		switch (game)
		{
		case GameSpec.FromGame.ER:
		{
			EventConfig eventConfig3;
			using (StreamReader input7 = File.OpenText("eldendata\\Base\\fogevents.txt"))
			{
				eventConfig3 = new DeserializerBuilder().Build().Deserialize<EventConfig>(input7);
				eventConfig3.MakeWarpCommands(events2);
			}
			new GameDataWriterE().Write(opt, annotationData, graph, mergedMods, outDir, events2, eventConfig3, notify);
			break;
		}
		case GameSpec.FromGame.DS3:
		{
			EventConfig eventConfig2;
			using (StreamReader input6 = File.OpenText("fogdist\\events.txt"))
			{
				eventConfig2 = deserializer.Deserialize<EventConfig>(input6);
			}
			if (opt["eventsyaml"] || opt["events"])
			{
				return result;
			}
			new GameDataWriter3().Write(opt, annotationData, graph, gameDir, outDir, events2, eventConfig2);
			break;
		}
		case GameSpec.FromGame.DS1:
			if (opt["dryrun"])
			{
				Console.WriteLine("Success (dry run)");
				return result;
			}
			new GameDataWriter().Write(opt, annotationData, graph, gameDir, game);
			break;
		}
		return result;
	}
}
