﻿// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace ExFat.DiscUtils.Tests
{
    using System.Linq;
    using Filesystem;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    [TestCategory("EntryFilesystem")]
    public class EntryFilesystemStructureTests
    {
        [TestMethod]
        [TestCategory("Read")]
        public void ReadFile()
        {
            using (var testEnvironment = new TestEnvironment())
            using (var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream))
            {
                var files = filesystem.EnumerateFileSystemEntries(filesystem.RootDirectory).ToArray();
                Assert.IsTrue(files.Any(f => f.Name == DiskContent.LongContiguousFileName));
            }
        }
    }
}