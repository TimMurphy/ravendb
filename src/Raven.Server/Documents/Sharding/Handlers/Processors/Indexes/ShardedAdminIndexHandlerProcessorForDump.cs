﻿using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Http;
using Raven.Server.Documents.Handlers.Admin.Processors.Indexes;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Indexes
{
    internal class ShardedAdminIndexHandlerProcessorForDump : AbstractAdminIndexHandlerProcessorForDump<ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedAdminIndexHandlerProcessorForDump([NotNull] ShardedDatabaseRequestHandler requestHandler)
            : base(requestHandler, requestHandler.ContextPool)
        {
        }

        protected override bool SupportsCurrentNode => false;

        protected override ValueTask ExecuteForCurrentNodeAsync() => throw new NotSupportedException();

        protected override Task ExecuteForRemoteNodeAsync(RavenCommand command)
        {
            var shardNumber = GetShardNumber();

            return RequestHandler.ShardExecutor.ExecuteSingleShardAsync(command, shardNumber);
        }
    }
}
