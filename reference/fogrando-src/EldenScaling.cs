using System;
using System.Collections.Generic;
using System.Linq;
using SoulsFormats;
using SoulsIds;

namespace FogMod;

public class EldenScaling
{
	private class ScalingData
	{
		public int ScalingBase { get; set; }

		public int NewScalingBase { get; set; }

		public int MaxTier { get; set; }

		public List<(int, int)> SectionPairs { get; set; }

		public Dictionary<string, List<string>> ScalingFields { get; set; }

		public Dictionary<string, List<double>> ScalingMatrix { get; set; }

		public Dictionary<string, List<double>> UniqueScalingMatrix { get; set; }
	}

	public class SpEffectValues
	{
		public Dictionary<(int, int), AreaScalingValue> Areas = new Dictionary<(int, int), AreaScalingValue>();
	}

	public class AreaScalingValue
	{
		public int RegularScaling { get; set; }

		public int FixedScaling { get; set; }

		public int UniqueRegularScaling { get; set; }

		public int UniqueFixedScaling { get; set; }
	}

	private readonly ParamDictionary Params;

	private readonly ScalingData eldenScaling = new ScalingData
	{
		ScalingBase = 7000,
		NewScalingBase = 7800000,
		MaxTier = 34,
		SectionPairs = null,
		ScalingMatrix = null,
		ScalingFields = new Dictionary<string, List<string>>
		{
			["health"] = new List<string> { "maxHpRate" },
			["stamina"] = new List<string> { "maxStaminaRate" },
			["staminadamage"] = new List<string> { "staminaAttackRate" },
			["damage"] = new List<string> { "physicsAttackPowerRate", "magicAttackPowerRate", "fireAttackPowerRate", "thunderAttackPowerRate", "darkAttackPowerRate" },
			["defense"] = new List<string> { "physicsDiffenceRate", "magicDiffenceRate", "fireDiffenceRate", "thunderDiffenceRate", "darkDiffenceRate" },
			["buildup"] = new List<string> { "registPoizonChangeRate", "registDiseaseChangeRate", "registBloodChangeRate", "registFreezeChangeRate", "registSleepChangeRate", "registMadnessChangeRate" },
			["xp"] = new List<string> { "haveSoulRate" }
		}
	};

	private static readonly List<double> eldenExps = new List<double>
	{
		0.0, 23.0, 43.0, 188.0, 233.0, 285.0, 487.0, 743.0, 769.0, 925.0,
		970.0, 1091.0, 1107.0, 1192.0, 1277.0, 1430.0, 1438.0, 1458.0, 1478.0, 1478.0,
		1492.0, 1506.0, 1520.0, 1534.0, 1548.0, 1562.0, 1576.0, 1590.0, 1604.0, 1618.0,
		1632.0, 1646.0, 1654.0, 1654.0
	};

	public static readonly List<double> EldenSoulScaling = eldenExps.Select((double exp) => Math.Pow(10.0, exp / 1000.0)).ToList();

	public EldenScaling(ParamDictionary Params)
	{
		this.Params = Params;
	}

	public Dictionary<int, int> InitializeEldenScaling()
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		Dictionary<string, List<double>> dictionary2 = new Dictionary<string, List<double>>();
		for (int i = 1; i <= 34; i++)
		{
			int num;
			if (i <= 20)
			{
				num = 7000 + 10 * i;
				dictionary[num] = i;
				dictionary[19350 + i] = i;
			}
			else
			{
				num = 20007000 + 10 * (i - 21);
				dictionary[num] = i;
				dictionary[num + 200] = i;
			}
			PARAM.Row row = Params["SpEffectParam"][num];
			foreach (KeyValuePair<string, List<string>> scalingField in eldenScaling.ScalingFields)
			{
				Util.AddMulti(dictionary2, scalingField.Key, (float)row[scalingField.Value[0]].Value);
			}
		}
		dictionary[7000] = 1;
		dictionary[20007340] = 28;
		dictionary[20007350] = 28;
		eldenScaling.SectionPairs = new List<(int, int)>();
		for (int j = 2; j <= eldenScaling.MaxTier; j++)
		{
			for (int k = 1; k < j; k++)
			{
				eldenScaling.SectionPairs.Add((k, j));
			}
		}
		double x = 1.275;
		Dictionary<string, double> dampenTypes = new Dictionary<string, double>
		{
			["damage"] = Math.Pow(x, 1.0 / 19.0),
			["health"] = Math.Pow(x, 1.0 / 15.0)
		};
		Dictionary<string, List<double>> dictionary3 = new Dictionary<string, List<double>>(dictionary2);
		bool flag = false;
		foreach (KeyValuePair<string, List<double>> item in dictionary2.Where((KeyValuePair<string, List<double>> m) => dampenTypes.ContainsKey(m.Key)))
		{
			double num2 = 1.0;
			double num3 = dampenTypes[item.Key];
			List<double> vals = item.Value.ToList();
			bool flag2 = false;
			for (int num4 = 0; num4 < vals.Count && (num4 != 20 || !(item.Key == "health")); num4++)
			{
				if (num4 == 21 && item.Key == "damage")
				{
					num3 = 1.0 / Math.Pow(1.5, 1.0 / 15.0);
					num2 /= 1.5;
					flag2 = false;
				}
				double value = vals[num4] / num2;
				double num5 = vals[num4] / (num2 * num3);
				if (flag2 && num5 <= vals[num4 - 1])
				{
					vals[num4] = value;
					if (flag)
					{
						Console.WriteLine($"Skipping {item.Key} {num4}: value {vals[num4 - 1]:f4}->{value:f4}->{num5:f4} with factor {num2:f4} ({item.Value[num4 - 1]:f4}->{item.Value[num4]:f4})");
					}
				}
				else
				{
					vals[num4] = num5;
					num2 *= num3;
					flag2 = true;
				}
			}
			if (flag)
			{
				Console.WriteLine(item.Key + " scaling: " + string.Join(", ", item.Value.Select((double v, int num6) => $"{num6}: {v:f4}->{vals[num6]:f4}")));
			}
			dictionary3[item.Key] = vals;
		}
		dictionary2["xp"] = EldenSoulScaling;
		eldenScaling.ScalingMatrix = makeScalingMatrix(dictionary2);
		eldenScaling.UniqueScalingMatrix = makeScalingMatrix(dictionary3);
		return dictionary;
		Dictionary<string, List<double>> makeScalingMatrix(Dictionary<string, List<double>> scalingMult)
		{
			Dictionary<string, List<double>> dictionary4 = new Dictionary<string, List<double>>();
			foreach (KeyValuePair<string, List<double>> item2 in scalingMult)
			{
				List<double> value2 = item2.Value;
				List<double> list = new List<double>();
				foreach (var (num6, num7) in eldenScaling.SectionPairs)
				{
					list.Add(value2[num7 - 1] / value2[num6 - 1]);
				}
				dictionary4[item2.Key] = list;
			}
			return dictionary4;
		}
	}

	public SpEffectValues EditScalingSpEffects()
	{
		SpEffectValues spEffectValues = new SpEffectValues();
		ScalingData d = eldenScaling;
		if (d.ScalingMatrix.Any((KeyValuePair<string, List<double>> e) => !d.ScalingFields.ContainsKey(e.Key) || e.Value.Count != d.SectionPairs.Count))
		{
			throw new Exception("Internal error: bad scaling values");
		}
		int newSpBase = d.NewScalingBase;
		PARAM.Row defaultSp = Params["SpEffectParam"][d.ScalingBase];
		PARAM.Row existSp = Params["SpEffectParam"][d.NewScalingBase];
		int num = d.SectionPairs.Select(((int, int) p) => p.Item2).Max();
		int index;
		bool invert;
		for (int num2 = 1; num2 <= num; num2++)
		{
			for (int num3 = 1; num3 <= num; num3++)
			{
				if (num2 != num3)
				{
					int num4 = d.SectionPairs.IndexOf((num2, num3));
					int num5 = d.SectionPairs.IndexOf((num3, num2));
					if (num4 == -1 && num5 == -1)
					{
						throw new Exception($"Internal error: no scaling values defined for section transfer {num2}->{num3}");
					}
					index = ((num4 == -1) ? num5 : num4);
					invert = num4 == -1;
					AreaScalingValue areaScalingValue = new AreaScalingValue();
					spEffectValues.Areas[(num2, num3)] = areaScalingValue;
					areaScalingValue.FixedScaling = fillFields(d.ScalingMatrix, includeXp: false);
					areaScalingValue.RegularScaling = fillFields(d.ScalingMatrix, includeXp: true);
					if (d.UniqueScalingMatrix != null)
					{
						areaScalingValue.UniqueFixedScaling = fillFields(d.UniqueScalingMatrix, includeXp: false);
						areaScalingValue.UniqueRegularScaling = fillFields(d.UniqueScalingMatrix, includeXp: true);
					}
					else
					{
						areaScalingValue.UniqueFixedScaling = areaScalingValue.FixedScaling;
						areaScalingValue.UniqueRegularScaling = areaScalingValue.RegularScaling;
					}
				}
			}
		}
		Params["SpEffectParam"].Rows.Sort((PARAM.Row a, PARAM.Row b) => a.ID.CompareTo(b.ID));
		return spEffectValues;
		PARAM.Row createCustomEffect()
		{
			int num6 = newSpBase++;
			PARAM.Row row;
			if (existSp == null)
			{
				row = GameEditor.AddRow(Params["SpEffectParam"], num6);
			}
			else
			{
				row = Params["SpEffectParam"][num6];
				if (row == null)
				{
					throw new Exception($"Error in merged SpEffectParam: {d.NewScalingBase} exists but {num6} doesn't");
				}
			}
			GameEditor.CopyRow(defaultSp, row);
			return row;
		}
		int fillFields(Dictionary<string, List<double>> scalingMatrix, bool includeXp)
		{
			PARAM.Row row = createCustomEffect();
			foreach (KeyValuePair<string, List<string>> scalingField in d.ScalingFields)
			{
				double num6 = scalingMatrix[scalingField.Key][index];
				if (invert)
				{
					num6 = 1.0 / num6;
				}
				foreach (string item in scalingField.Value)
				{
					if (includeXp || !(scalingField.Key == "xp"))
					{
						row[item].Value = (float)num6;
					}
				}
			}
			return row.ID;
		}
	}
}
