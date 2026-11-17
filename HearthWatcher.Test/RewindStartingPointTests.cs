using System;
using System.IO;
using System.Reflection;
using HearthWatcher.LogReader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HearthWatcher.Test
{
	[TestClass]
	public class RewindStartingPointTests
	{
		private string _directory = string.Empty;
		private readonly DateTime _start = DateTime.Today.AddMinutes(1);
		private readonly DateTime _rewind = DateTime.Today.AddMinutes(2);
		private readonly DateTime _complete = DateTime.Today.AddMinutes(3);

		[TestInitialize]
		public void SetUp()
		{
			_directory = Path.Combine(Path.GetTempPath(), "hdt-rewind-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_directory);
			Write("LoadingScreen", _start, "Gameplay.Start");
			Write("Power", _complete, "GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE");
		}

		[TestCleanup]
		public void CleanUp() => Directory.Delete(_directory, true);

		private void Write(string log, DateTime time, string content)
		{
			// Include a preceding CRLF: the production reader scans backward by line.
			File.WriteAllText(Path.Combine(_directory, log + ".log"),
				"D 00:00:00.0000000 Header\r\nD " + time.ToString("HH:mm:ss.fffffff") + " " + content + "\r\n");
		}

		private DateTime StartingPoint(DateTime? rewind)
		{
			var watcher = new LogWatcher(new[] {new LogWatcherInfo("Power"), new LogWatcherInfo("LoadingScreen")});
			return (DateTime)typeof(LogWatcher).GetMethod("GetStartingPoint", BindingFlags.NonPublic | BindingFlags.Instance)
				.Invoke(watcher, new object[] {_directory, rewind});
		}

		[TestMethod]
		public void LethalRewindRebuildsMatchBeforeComplete() => Assert.AreEqual(_start, StartingPoint(_rewind));

		[TestMethod]
		public void OrdinaryStartupKeepsCompletedGameBoundary() => Assert.AreEqual(_complete, StartingPoint(null));

		[TestMethod]
		public void NonlethalRewindKeepsGameplayStart()
		{
			Write("Power", _start.AddSeconds(-1), "tag=STATE value=COMPLETE");
			Assert.AreEqual(_start, StartingPoint(_rewind));
		}

		[TestMethod]
		public void MissingGameplayStartKeepsNativeBoundary()
		{
			File.WriteAllText(Path.Combine(_directory, "LoadingScreen.log"), "");
			Assert.AreEqual(_complete, StartingPoint(_rewind));
		}

		[TestMethod]
		public void NewerMatchCannotBePulledBackByOldRewind()
		{
			var newerStart = _complete.AddMinutes(1);
			Write("LoadingScreen", newerStart, "Gameplay.Start");
			Assert.AreEqual(newerStart, StartingPoint(_rewind));
		}

		[TestMethod]
		public void BoundaryBeforeRewindRemainsUnchanged() => Assert.AreEqual(_complete, StartingPoint(_complete.AddSeconds(1)));
	}
}
