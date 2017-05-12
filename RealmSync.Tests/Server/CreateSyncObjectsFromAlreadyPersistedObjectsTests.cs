﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using RealmSync.Server;
using RealmSync.SyncService;
using RealmSync.Tests.Server.Models;

using FluentAssertions;
using NUnit.Framework;

namespace RealmSync.Tests.Server
{
    [TestFixture]
    public class CreateSyncObjectsFromAlreadyPersistedObjectsTests : TestBase
    {
        private Func<LocalDbContext> _contextFunc;
        private RealmSyncServerProcessor _processor;


        public CreateSyncObjectsFromAlreadyPersistedObjectsTests()
        {
            _contextFunc = () => new LocalDbContext(new ShareEverythingRealmSyncServerConfiguration(typeof(DbSyncObject)));
        }

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            _processor = new RealmSyncServerProcessor(_contextFunc, new ShareEverythingRealmSyncServerConfiguration(typeof(DbSyncObject)));
        }

        [Test]
        public void KeyType()
        {
            var db = _contextFunc();

            db.GetKeyType(nameof(DbSyncObject)).Should().Be(typeof(string));
            db.GetKeyType(nameof(IdGuidObject)).Should().Be(typeof(Guid));
            db.GetKeyType(nameof(IdIntObject)).Should().Be(typeof(int));
        }


        [Test]
        public void GetObjectByKey()
        {
            var db = _contextFunc();
            db.DbSyncObjects.Add(new DbSyncObject()
            {
                Id = "2",
                Text = "x",
            });
            var g = Guid.NewGuid();
            db.IdGuidObjects.Add(new IdGuidObject()
            {
                Id = g,
                Text = "b"
            });
            db.IdIntObjects.Add(new IdIntObject()
            {
                Id = 4,
                Text = "5",
            });
            db.SaveChanges();

            var db2 = _contextFunc();
            ((DbSyncObject)db2.GetObjectByKey(nameof(DbSyncObject), "2")).Text.Should().BeEquivalentTo("x");
            ((IdGuidObject)db2.GetObjectByKey(nameof(IdGuidObject), g.ToString())).Text.Should().BeEquivalentTo("b");
            ((IdIntObject)db2.GetObjectByKey(nameof(IdIntObject), "4")).Text.Should().BeEquivalentTo("5");
        }


        [Test]
        public void Attach_NewObject()
        {
            var db = _contextFunc();
            db.EnableSyncTracking = false;
            db.DbSyncObjects.Add(new DbSyncObject()
            {
                Id = "2",
                Text = "x",
            });
            db.SaveChanges();

            db.CreateSyncStatusContext().SyncStatusServerObjects.Count().Should().Be(0);

            var res = _processor.Download(
                new DownloadDataRequest()
                {
                    Types = new[]
                    {
                        nameof(DbSyncObject)
                    },
                    LastChangeTime = new Dictionary<string, DateTimeOffset>() { { "all", DateTimeOffset.MinValue } },
                },
                new SyncUser());
            res.ChangedObjects.Count.Should().Be(0);

            db.EnableSyncTracking = true;
            db.AttachObject(nameof(DbSyncObject), "2");
            db.CreateSyncStatusContext().SyncStatusServerObjects.Count().Should().Be(1);

            var sync = db.CreateSyncStatusContext();
            var res2 = _processor.Download(
                new DownloadDataRequest()
                {
                    Types = new[]
                    {
                        nameof(DbSyncObject)
                    }
                },
                new SyncUser());

            string.Join(", ", res2.ChangedObjects)
                .Should().BeEquivalentTo("Type: DbSyncObject, Key: 2, SerializedObject: { \"Text\": \"x\", \"Tags\": null, \"Id\": \"2\"}");
        }


        [Test]
        public void Attach_UpdatedObject()
        {
            var db = _contextFunc();
            var obj = new DbSyncObject()
            {
                Id = "2",
                Text = "x",
                Tags = "c",
            };
            db.DbSyncObjects.Add(obj);
            db.SaveChanges();

            Thread.Sleep(10);
            db.CreateSyncStatusContext().SyncStatusServerObjects.Count().Should().Be(1);

            db.EnableSyncTracking = false;
            obj.Text = "qwe";
            db.SaveChanges();

            var res = _processor.Download(
                new DownloadDataRequest()
                {
                    Types = new[]
                    {
                        nameof(DbSyncObject)
                    },
                    LastChangeTime = DateTimeOffset.MinValue.ToDictionary(),
                },
                new SyncUser());
            string.Join(", ", res.ChangedObjects)
                .Should().BeEquivalentTo("Type: DbSyncObject, Key: 2, SerializedObject: { \"Text\": \"x\", \"Tags\": \"c\", \"Id\": \"2\"}");

            db.AttachObject(nameof(DbSyncObject), "2");
            db.CreateSyncStatusContext().SyncStatusServerObjects.Count().Should().Be(1);
            var syncStatus = db.CreateSyncStatusContext().SyncStatusServerObjects.First();
            syncStatus.ColumnChangeDates[nameof(obj.Text)].Should()
                .BeAfter(syncStatus.ColumnChangeDates[nameof(obj.Tags)]);

            var res2 = _processor.Download(
                new DownloadDataRequest()
                {
                    Types = new[]
                    {
                        nameof(DbSyncObject)
                    }
                },
                new SyncUser());

            string.Join(", ", res2.ChangedObjects)
                .Should().BeEquivalentTo("Type: DbSyncObject, Key: 2, SerializedObject: { \"Text\": \"qwe\", \"Tags\": \"c\", \"Id\": \"2\"}");
        }


        [Test]
        public void Attach_DeletedObject()
        {
            var time = DateTimeOffset.UtcNow;
            Thread.Sleep(10);

            var db = _contextFunc();
            var obj = new DbSyncObject()
            {
                Id = "2",
                Text = "x",
                Tags = "c"
            };
            db.DbSyncObjects.Add(obj);
            db.SaveChanges();

            db.CreateSyncStatusContext().SyncStatusServerObjects.Count().Should().Be(1);

            db.EnableSyncTracking = false;
            db.DbSyncObjects.Remove(obj);
            db.SaveChanges();

            
            var res = _processor.Download(
                new DownloadDataRequest()
                {
                    Types = new[]
                    {
                        nameof(DbSyncObject)
                    },
                    LastChangeTime = new Dictionary<string, DateTimeOffset>() { { "all", time } },
                },
                new SyncUser());
            string.Join(", ", res.ChangedObjects)
                .Should().BeEquivalentTo("Type: DbSyncObject, Key: 2, SerializedObject: { \"Text\": \"x\", \"Tags\": \"c\", \"Id\": \"2\"}");

            db.AttachDeletedObject(nameof(DbSyncObject), "2");
            var res2 = _processor.Download(
                new DownloadDataRequest()
                {
                    Types = new[]
                    {
                        nameof(DbSyncObject)
                    },
                    LastChangeTime = new Dictionary<string, DateTimeOffset>() { { "all", time } },
                },
                new SyncUser());

            string.Join(", ", res2.ChangedObjects)
                .Should().BeEquivalentTo("Type: DbSyncObject, Key: 2, Deleted");
        }
    }

}
