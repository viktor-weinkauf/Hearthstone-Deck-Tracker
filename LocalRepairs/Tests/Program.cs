using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using ArenaComparisonBridge;
using HearthWatcher;
using HearthWatcher.LogReader;
using Newtonsoft.Json.Linq;

class Program
{
	static int checks;
	static void Check(bool value, string message)
	{
		if (!value) throw new Exception(message);
		checks++;
		Console.WriteLine("PASS " + message);
	}

	static DateTime At(string time) => new LogLine("Power", "D " + time + " GameState").Time;
	static LogWatcher MakeWatcher() => new LogWatcher(new[] {
		new LogWatcherInfo("Power"), new LogWatcherInfo("LoadingScreen") });
	static DateTime Start(LogWatcher watcher, string directory) => (DateTime)typeof(LogWatcher)
		.GetMethod("GetStartingPoint", BindingFlags.Instance | BindingFlags.NonPublic)
		.Invoke(watcher, new object[] { directory });

	static void Main(string[] args)
	{
		var installed = args.Length > 1 ? args[1] : Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HearthstoneDeckTracker", "current");
		AppDomain.CurrentDomain.AssemblyResolve += (_, request) => {
			var name = new AssemblyName(request.Name).Name;
			var path = Path.Combine(installed, name + (name == "HearthstoneDeckTracker" ? ".exe" : ".dll"));
			return File.Exists(path) ? Assembly.LoadFrom(path) : null;
		};
		CheckInstalledLayout();
		var approved = new JObject { ["schema_version"] = 1, ["hdt_version"] = "test",
			["hdt_sha256"] = "abc", ["hearthwatcher_sha256"] = "def" };
		Check(RewindReplayRepair.IsCompatible("test", "abc", "def", approved), "Matching tested binaries accepted");
		Check(!RewindReplayRepair.IsCompatible("test", "abc", "def", null), "Missing approval rejected");
		Check(!RewindReplayRepair.IsCompatible("new", "abc", "def", approved), "New HDT version rejected");
		Check(!RewindReplayRepair.IsCompatible("test", "changed", "def", approved), "Same-version HDT replacement rejected");
		Check(!RewindReplayRepair.IsCompatible("test", "abc", "changed", approved), "Changed watcher rejected");
		// No game process, network, replay upload, or live HDT data writes.
		var fixture = Path.GetFullPath(args[0]);
		var snapshot = Path.Combine(fixture, "at-reset");
		Directory.CreateDirectory(snapshot);
		var power = File.ReadAllLines(Path.Combine(fixture, "Power.log"));
		var loading = File.ReadAllLines(Path.Combine(fixture, "LoadingScreen.log"));
		var start = At("16:13:35.2547857");
		var play = At("16:27:43");
		var ending = At("16:27:48.3844140");
		var cutoff = At("16:27:49.1");
		File.WriteAllLines(Path.Combine(snapshot, "Power.log"), power.Where(l => new LogLine("Power", l).Time <= cutoff));
		File.WriteAllLines(Path.Combine(snapshot, "LoadingScreen.log"), loading.Where(l => new LogLine("LoadingScreen", l).Time <= cutoff));

		var watcher = MakeWatcher();
		var original = Start(watcher, snapshot);
		Console.WriteLine("Unpatched installed reader starts at " + original.ToString("HH:mm:ss.fffffff"));
		Check(original == ending, "Reproduce installed reader skipping to fatal result");
		Check(original > start, "Broken start skips game creation and all ten turns");

		DateTime? rewind = play;
		RewindReplayRepair.Attach(watcher, () => rewind, Console.WriteLine);
		try
		{
			Check(Start(watcher, snapshot) == start, "Patched installed reader restarts at Gameplay.Start");
			Check(Start(MakeWatcher(), snapshot) == original, "Other reader instances are unchanged");
			rewind = null;
			Check(Start(watcher, snapshot) == original, "Ordinary startup after a completed game is unchanged");
			rewind = play;
			Check(RewindReplayRepair.Select(start, start, rewind) == start, "Nonlethal rewind keeps its already-correct entry point");
			Check(RewindReplayRepair.Select(ending, DateTime.MinValue, rewind) == ending, "Missing match-start evidence leaves native behavior intact");
			Check(RewindReplayRepair.Select(ending, ending.AddMinutes(1), rewind) == ending, "Old rewind cannot pull a new match backward");
			Check(RewindReplayRepair.Select(ending, start, ending.AddMinutes(1)) == ending, "Completed game before rewind needs no correction");

			// Exercise the actual installed file-reader with the selected start.
			// Read-only locks prevent its startup cleanup from renaming the fixture.
			var selected = Start(watcher, snapshot);
			using (var locked = new FileStream(Path.Combine(snapshot, "Power.log"), FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				var reader = new LogFileWatcher(new LogWatcherInfo("Power"));
				var lines = new List<LogLine>();
				reader.Start(selected, snapshot);
				try
				{
					var deadline = DateTime.UtcNow.AddSeconds(10);
					while (DateTime.UtcNow < deadline)
					{
						lines.AddRange(reader.Collect());
						if (lines.Any(l => l.Line.Contains("tag=STATE value=COMPLETE"))) break;
						Thread.Sleep(50);
					}
				}
				finally { reader.Stop().GetAwaiter().GetResult(); }
				Check(lines.Any(l => l.Line.Contains("CREATE_GAME")), "Corrected reread includes game creation");
				Check(lines.Any(l => l.Line.Contains("PlayerID=1")), "Corrected reread includes player identity");
				Check(lines.Any(l => l.Line.Contains("tag=TURN value=20")), "Corrected reread includes opponent turn ten");
				Check(lines.Any(l => l.Line.Contains("tag=STATE value=COMPLETE")), "Corrected reread includes the fatal result");
				Check(File.Exists(Path.Combine(snapshot, "Power.log")), "Fixture remains intact after actual reader test");
			}
		}
		finally { RewindReplayRepair.Uninstall(); }
		Check(Start(watcher, snapshot) == original, "Unloading removes the patch completely");
		Console.WriteLine(checks + " checks passed against installed HearthWatcher");
	}

	static void CheckInstalledLayout()
	{
		var flags = BindingFlags.NonPublic | BindingFlags.Instance;
		Check(typeof(Hearthstone_Deck_Tracker.Core).GetField("LogWatcherManger",
			BindingFlags.Static | BindingFlags.NonPublic) != null, "HDT manager field still exists");
		var manager = typeof(Hearthstone_Deck_Tracker.LogReader.LogWatcherManager);
		Check(manager.GetField("_logWatcher", flags)?.FieldType == typeof(LogWatcher), "HDT reader binding is compatible");
		Check(manager.GetField("IgnoredTimeRanges") != null, "HDT rewind ranges are available");
	}
}
