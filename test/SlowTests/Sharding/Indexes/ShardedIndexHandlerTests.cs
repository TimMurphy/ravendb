﻿using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.Exceptions.Sharding;
using SlowTests.Core.Utils.Entities;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Sharding.Indexes
{
    public class ShardedIndexHandlerTests : RavenTestBase
    {
        public ShardedIndexHandlerTests(ITestOutputHelper output) : base(output)
        {
        }

        [RavenFact(RavenTestCategory.Indexes | RavenTestCategory.Sharding)]
        public async Task CanGetIndexStatistics()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenAsyncSession())
                {
                    for (var i = 0; i < 10; i++)
                    {
                        var id = $"Raven/{i}";

                        var user = new User { Name = $"Raven-{i}" };
                        await session.StoreAsync(user, id);
                        await session.SaveChangesAsync();
                    }
                }

                await new UserIndex().ExecuteAsync(store);

                var indexStats = await store.Maintenance.ForNode("A").ForShardWithProxy(0).SendAsync(new GetIndexesStatisticsOperation());
                Assert.NotNull(indexStats);
                Assert.Equal(1, indexStats.Length);
                Assert.Equal("UserIndex", indexStats[0].Name);
                Assert.Equal(1, indexStats[0].Collections.Count);
                Assert.True(indexStats[0].Collections.ContainsKey("Users"));
            }
        }

        [RavenFact(RavenTestCategory.Indexes | RavenTestCategory.Sharding)]
        public async Task CanGetIndexesStatus()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenAsyncSession())
                {
                    for (var i = 0; i < 10; i++)
                    {
                        var id = $"Raven/{i}";

                        var user = new User { Name = $"Raven-{i}" };
                        await session.StoreAsync(user, id);
                        await session.SaveChangesAsync();
                    }
                }

                await new UserIndex().ExecuteAsync(store);

                var indexStats = await store.Maintenance.ForNode("A").ForShardWithProxy(0).SendAsync(new GetIndexingStatusOperation());
                Assert.NotNull(indexStats);
                Assert.Equal(IndexRunningStatus.Running, indexStats.Status);
                Assert.Equal(1, indexStats.Indexes.Length);
                Assert.Equal("UserIndex", indexStats.Indexes[0].Name);
            }
        }

        [RavenFact(RavenTestCategory.Indexes | RavenTestCategory.Sharding)]
        public void Index_With_OutputReduceToCollection_ShouldThrow()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                var e = Assert.Throws<NotSupportedInShardingException>(() => new User_OutputReduceToCollection_Index().Execute(store));

                Assert.Contains("Index with output reduce to collection is not supported in sharding.", e.Message);
            }
        }

        private class UserIndex : AbstractIndexCreationTask<User>
        {
            public UserIndex()
            {
                Map = users => from user in users
                               select new
                               {
                                   user.Name
                               };
            }
        }

        private class User_OutputReduceToCollection_Index : AbstractIndexCreationTask<User, User_OutputReduceToCollection_Index.Result>
        {
            public class Result
            {
                public string Name { get; set; }

                public int Count { get; set; }
            }

            public User_OutputReduceToCollection_Index()
            {
                Map = users => from user in users
                               select new
                               {
                                   user.Name,
                                   Count = 1
                               };

                Reduce = results => from result in results
                                    group result by result.Name into g
                                    select new
                                    {
                                        Name = g.Key,
                                        Count = g.Sum(x => x.Count)
                                    };

                OutputReduceToCollection = "Results";
            }
        }
    }
}
