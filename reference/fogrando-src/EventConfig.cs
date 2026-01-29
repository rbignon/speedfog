using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SoulsIds;
using YamlDotNet.Serialization;

namespace FogMod;

public class EventConfig
{
	public class NewEvent : AnnotationData.Taggable
	{
		public int ID { get; set; }

		public string Name { get; set; }

		public string Comment { get; set; }

		public List<string> Commands { get; set; }
	}

	public class EventSpec : Events.AbstractEventSpec
	{
		public List<EventTemplate> Template { get; set; }
	}

	public class EventTemplate
	{
		public string Fog { get; set; }

		public string FogSfx { get; set; }

		public string Warp { get; set; }

		public string WarpID { get; set; }

		public string GuestWarpID { get; set; }

		public string EntranceWarpID { get; set; }

		public string ExtraNameID { get; set; }

		public string EvergaolStart { get; set; }

		public bool ReturnWarp { get; set; }

		public List<string> ArrivalCutscene { get; set; }

		public string TriggerRegion { get; set; }

		public string TriggerCommand { get; set; }

		public string BossCutscene { get; set; }

		public string Invincibility { get; set; }

		public string Custom { get; set; }

		public string BossEdit { get; set; }

		public Feature Feature { get; set; }

		public string Sfx { get; set; }

		public string SetFlag { get; set; }

		public string SetFlagIf { get; set; }

		public string SetFlagArea { get; set; }

		public string WarpReplace { get; set; }

		public int RepeatWarpObject { get; set; }

		public int RepeatWarpFlag { get; set; }

		public string MoveNPC { get; set; }

		public string MoveNPCHelper { get; set; }

		public string ShowNight { get; set; }

		public string ShowBonfire { get; set; }

		public string SegmentCopy { get; set; }

		public string SegmentCopyMaps { get; set; }

		public int CopyTo { get; set; }

		public bool Instance { get; set; }

		public string Remove { get; set; }

		public List<string> Removes { get; set; }

		public List<string> RemoveOpts { get; set; }

		public List<Events.EventAddCommand> Add { get; set; }

		public string Replace { get; set; }

		public List<Events.EventReplaceCommand> Replaces { get; set; }

		public string Comment { get; set; }
	}

	public class FogEdit
	{
		public bool CreateSfx = true;

		public List<FlagEdit> FlagEdits = new List<FlagEdit>();

		public int Sfx { get; set; }

		public int RepeatWarpObject { get; set; }

		public int RepeatWarpFlag { get; set; }
	}

	public class FlagEdit
	{
		public int SetFlag { get; set; }

		public int SetFlagIf { get; set; }

		public int SetFlagArea { get; set; }
	}

	public class EvergaolEdit
	{
		public int DefeatFlag { get; set; }

		public int ActiveFlag { get; set; }

		public int NearbyGroup { get; set; }

		public int Asset { get; set; }
	}

	public class WarpArg
	{
		public string Name { get; set; }

		public string RegionArg { get; set; }

		public string MapArg { get; set; }
	}

	public class WarpCommand
	{
		public int RegionPos { get; private set; }

		public int MapPos { get; private set; }

		public bool MapParts { get; private set; }

		public static WarpCommand FromArg(WarpArg arg, Events events)
		{
			if (arg.RegionArg == null)
			{
				return new WarpCommand
				{
					RegionPos = -1,
					MapPos = -1
				};
			}
			int item = events.LookupArgIndex(arg.Name, arg.RegionArg).Item1;
			if (arg.MapArg == null)
			{
				return new WarpCommand
				{
					RegionPos = item,
					MapPos = -1
				};
			}
			var (mapPos, num) = events.LookupArgIndex(arg.Name, arg.MapArg);
			return new WarpCommand
			{
				RegionPos = item,
				MapPos = mapPos,
				MapParts = (num == 1)
			};
		}
	}

	public static readonly Regex PhraseRe = new Regex("\\s*;\\s*");

	public List<NewEvent> NewEvents { get; set; }

	public List<WarpArg> WarpArgs { get; set; }

	public List<EventSpec> Events { get; set; }

	[YamlIgnore]
	public Dictionary<string, WarpCommand> WarpCommands { get; set; }

	public void MakeWarpCommands(Events events)
	{
		if (WarpArgs == null)
		{
			WarpCommands = new Dictionary<string, WarpCommand>();
		}
		if (WarpCommands == null)
		{
			WarpCommands = WarpArgs.ToDictionary((WarpArg w) => w.Name, (WarpArg w) => WarpCommand.FromArg(w, events));
		}
	}
}
