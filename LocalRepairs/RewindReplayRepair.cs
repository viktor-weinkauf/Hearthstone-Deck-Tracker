using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using HearthWatcher;
using HearthWatcher.LogReader;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.LogReader;
using Newtonsoft.Json.Linq;

namespace ArenaComparisonBridge
{
	// HDT normally rebuilds a rewind by resetting and rereading the logs.
	// If the reroll is lethal, STATE=COMPLETE can already be in Power.log
	// when the reader restarts. Its ordinary startup heuristic then skips
	// this match, losing the players, mode, turns and replay. Only change
	// that heuristic while HDT has an active rewind range for this reader.
	internal static class RewindReplayRepair
	{
		const string PatchId = "local.arena-comparison.rewind-replay";
		static string statusPath;
		const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
		static Harmony harmony;
		static LogWatcher target;
		static Func<DateTime?> earliestRewind;
		static Action<string> report;
		static PropertyInfo loadingScreen;
		static string hdtVersion;
		static bool active;

		internal static void Install(string diagnosticsPath)
		{
			statusPath = diagnosticsPath;
			try
			{
				hdtVersion = typeof(Core).Assembly.GetName().Version.ToString();
				// Approval is tied to tested binary hashes, not just a version label.
				var manifestPath = Path.Combine(Path.GetDirectoryName(typeof(RewindReplayRepair).Assembly.Location),
					"rewind-compatibility.json");
				var manifest = File.Exists(manifestPath) ? JObject.Parse(File.ReadAllText(manifestPath)) : null;
				if (!IsCompatible(hdtVersion, Hash(typeof(Core).Assembly.Location),
					Hash(typeof(LogWatcher).Assembly.Location), manifest))
				{
					WriteStatus("Not installed: HDT binaries require compatibility tests; run LocalRepairs/Test-Installed.ps1");
					return;
				}
				var manager = (LogWatcherManager)typeof(Core)
					.GetField("LogWatcherManger", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
				var watcher = (LogWatcher)typeof(LogWatcherManager)
					.GetField("_logWatcher", PrivateInstance).GetValue(manager);
				Attach(watcher, () => manager.IgnoredTimeRanges.Count == 0
					? (DateTime?)null : manager.IgnoredTimeRanges.Min(r => r.Start), WriteStatus);
			}
			catch (Exception ex)
			{
				Uninstall();
				WriteStatus("Not installed: " + ex.GetType().Name + ": " + ex.Message);
			}
		}

		internal static bool IsCompatible(string version, string hdtHash, string watcherHash, JObject manifest)
		{
			return manifest != null && (int?)manifest["schema_version"] == 1
				&& (string)manifest["hdt_version"] == version
				&& string.Equals((string)manifest["hdt_sha256"], hdtHash, StringComparison.OrdinalIgnoreCase)
				&& string.Equals((string)manifest["hearthwatcher_sha256"], watcherHash, StringComparison.OrdinalIgnoreCase);
		}

		internal static string Hash(string path)
		{
			using (var stream = File.OpenRead(path))
			using (var sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
		}

		// Also used by the isolated test host with the installed HearthWatcher DLL.
		// The host supplies its own reader/ranges; it never accesses the live game.
		internal static void Attach(LogWatcher watcher, Func<DateTime?> rewindStart, Action<string> diagnostics)
		{
			Uninstall();
			var method = typeof(LogWatcher).GetMethod("GetStartingPoint", PrivateInstance,
				null, new[] { typeof(string) }, null);
			loadingScreen = typeof(LogWatcher).GetProperty("LoadingScreenLogWatcher", PrivateInstance);
			if (method == null || method.ReturnType != typeof(DateTime) || loadingScreen == null)
				throw new NotSupportedException("Log-reader signature changed");
			target = watcher ?? throw new ArgumentNullException(nameof(watcher));
			earliestRewind = rewindStart;
			report = diagnostics;
			harmony = new Harmony(PatchId);
			harmony.Patch(method, postfix: new HarmonyMethod(typeof(RewindReplayRepair), nameof(AfterStartingPoint)));
			active = true;
			report?.Invoke("Active: Rewind match-start recovery installed");
		}

		static void AfterStartingPoint(LogWatcher __instance, string logDirectory, ref DateTime __result)
		{
			if (!ReferenceEquals(__instance, target)) return;
			try
			{
				var rewind = earliestRewind?.Invoke();
				if (!rewind.HasValue || __result <= rewind.Value) return;
				var reader = (LogFileWatcher)loadingScreen.GetValue(__instance);
				var start = reader.FindEntryPoint(logDirectory, "Gameplay.Start");
				var selected = Select(__result, start, rewind);
				if (selected == __result) return;
				var skipped = __result;
				__result = selected;
				report?.Invoke("Recovered Rewind entry point: " + skipped.ToString("HH:mm:ss.fffffff")
					+ " -> " + selected.ToString("HH:mm:ss.fffffff"));
			}
			catch (Exception ex)
			{
				// Never break HDT's normal pipeline if the compatibility hook fails.
				report?.Invoke("Rewind entry-point correction failed: " + ex.GetType().Name);
			}
		}

		internal static DateTime Select(DateTime original, DateTime matchStart, DateTime? rewind)
		{
			if (!rewind.HasValue || matchStart == DateTime.MinValue || matchStart > rewind.Value
				|| original <= rewind.Value || original <= matchStart) return original;
			return matchStart;
		}

		internal static void Uninstall()
		{
			harmony?.UnpatchAll(PatchId);
			harmony = null;
			target = null;
			earliestRewind = null;
			active = false;
			if (report != null) report("Inactive: bridge unloaded");
			report = null;
		}

		static void WriteStatus(string message)
		{
			try
			{
				if (string.IsNullOrEmpty(statusPath)) return;
				Directory.CreateDirectory(Path.GetDirectoryName(statusPath));
				var text = new JObject { ["active"] = active, ["hdt_version"] = hdtVersion,
					["repair_version"] = "2", ["observed_utc"] = DateTime.UtcNow.ToString("o"),
					["status"] = message }.ToString();
				File.WriteAllText(statusPath + ".tmp", text);
				if (File.Exists(statusPath)) File.Replace(statusPath + ".tmp", statusPath, null);
				else File.Move(statusPath + ".tmp", statusPath);
			}
			catch { }
		}
	}
}
