using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FogMod;

public class Util
{
	public static void AddMulti<K, V>(IDictionary<K, List<V>> dict, K key, V value)
	{
		if (!dict.ContainsKey(key))
		{
			dict[key] = new List<V>();
		}
		dict[key].Add(value);
	}

	public static void AddMulti<K, V>(IDictionary<K, List<V>> dict, K key, IEnumerable<V> values)
	{
		if (!dict.ContainsKey(key))
		{
			dict[key] = new List<V>();
		}
		dict[key].AddRange(values);
	}

	public static void AddMulti<K, V>(IDictionary<K, HashSet<V>> dict, K key, V value)
	{
		if (!dict.ContainsKey(key))
		{
			dict[key] = new HashSet<V>();
		}
		dict[key].Add(value);
	}

	public static void AddMulti<K, V>(IDictionary<K, HashSet<V>> dict, K key, IEnumerable<V> values)
	{
		if (!dict.ContainsKey(key))
		{
			dict[key] = new HashSet<V>();
		}
		dict[key].UnionWith(values);
	}

	public static void AddMulti<K, V>(IDictionary<K, SortedSet<V>> dict, K key, V value)
	{
		if (!dict.ContainsKey(key))
		{
			dict[key] = new SortedSet<V>();
		}
		dict[key].Add(value);
	}

	public static void AddMulti<K, V, V2, T>(IDictionary<K, T> dict, K key, V value, V2 value2) where T : IDictionary<V, V2>, new()
	{
		if (!dict.ContainsKey(key))
		{
			dict[key] = new T();
		}
		T val = dict[key];
		val[value] = value2;
	}

	public static void Shuffle<T>(Random random, IList<T> list)
	{
		for (int i = 0; i < list.Count - 1; i++)
		{
			int index = random.Next(i, list.Count);
			T value = list[i];
			list[i] = list[index];
			list[index] = value;
		}
	}

	public static T Choice<T>(Random random, IList<T> list)
	{
		return list[random.Next(list.Count)];
	}

	public static List<T> ChoiceN<T>(Random random, IList<T> list, int n)
	{
		if (n == 1)
		{
			return new List<T> { Choice(random, list) };
		}
		List<T> list2 = list.ToList();
		Shuffle(random, list2);
		if (list2.Count > n)
		{
			list2.RemoveRange(n, list2.Count - n);
		}
		return list2;
	}

	public static T WeightedChoice<T>(Random random, IList<T> list, Func<T, float> weightFunc)
	{
		List<float> list2 = list.Select(weightFunc).ToList();
		double num = list2.Sum();
		double num2 = random.NextDouble() * num;
		double num3 = 0.0;
		for (int i = 0; i < list.Count(); i++)
		{
			num3 += (double)list2[i];
			if (num3 > num2)
			{
				return list[i];
			}
		}
		return list[list.Count() - 1];
	}

	public static void CopyAll<T>(T source, T target)
	{
		Type typeFromHandle = typeof(T);
		PropertyInfo[] properties = typeFromHandle.GetProperties();
		foreach (PropertyInfo propertyInfo in properties)
		{
			PropertyInfo property = typeFromHandle.GetProperty(propertyInfo.Name);
			if (propertyInfo.CanWrite)
			{
				property.SetValue(target, propertyInfo.GetValue(source, null), null);
			}
			else if (propertyInfo.PropertyType.IsArray)
			{
				Array array = (Array)propertyInfo.GetValue(source);
				Array.Copy(array, (Array)property.GetValue(target), array.Length);
			}
		}
	}

	public static int SearchBytes(byte[] array, byte[] candidate)
	{
		for (int i = 0; i < array.Length; i++)
		{
			if (IsMatch(array, i, candidate))
			{
				return i;
			}
		}
		return -1;
	}

	private static bool IsMatch(byte[] array, int position, byte[] candidate)
	{
		if (candidate.Length > array.Length - position)
		{
			return false;
		}
		for (int i = 0; i < candidate.Length; i++)
		{
			if (array[position + i] != candidate[i])
			{
				return false;
			}
		}
		return true;
	}
}
