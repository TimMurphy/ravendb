﻿using System.Threading.Tasks;
using Raven.Client.ServerWide;
using Raven.Server.Routing;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Web;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Handlers.Debugging
{
    public class DatabaseRecordHandler : RequestHandler
    {
        internal static readonly string[] FieldsThatShouldBeExposedForDebug = new string[]
        {
            nameof(DatabaseRecord.DatabaseName),
            nameof(DatabaseRecord.Encrypted),
            nameof(DatabaseRecord.Disabled),
            nameof(DatabaseRecord.EtagForBackup),
            nameof(DatabaseRecord.DeletionInProgress),
            nameof(DatabaseRecord.DatabaseState),
            nameof(DatabaseRecord.Topology),
            nameof(DatabaseRecord.ConflictSolverConfig),
            nameof(DatabaseRecord.Sorters),
            nameof(DatabaseRecord.Indexes),
            nameof(DatabaseRecord.IndexesHistory),
            nameof(DatabaseRecord.AutoIndexes),
            nameof(DatabaseRecord.Settings),
            nameof(DatabaseRecord.Revisions),
            nameof(DatabaseRecord.RevisionsForConflicts),
            nameof(DatabaseRecord.Expiration),
            nameof(DatabaseRecord.Refresh),
            nameof(DatabaseRecord.PeriodicBackups),
            nameof(DatabaseRecord.ExternalReplications),
            nameof(DatabaseRecord.SinkPullReplications),
            nameof(DatabaseRecord.HubPullReplications),
            nameof(DatabaseRecord.RavenEtls),
            nameof(DatabaseRecord.SqlEtls),
            nameof(DatabaseRecord.Client),
            nameof(DatabaseRecord.Studio),
            nameof(DatabaseRecord.TruncatedClusterTransactionCommandsCount),
            nameof(DatabaseRecord.UnusedDatabaseIds),
        };

        [RavenAction("/debug/database-record", "GET", AuthorizationStatus.ValidUser, IsDebugInformationEndpoint = true)]
        public Task GetDatabaseRecord()
        {
            var databaseName = GetStringQueryString("name");
            var djv = new DynamicJsonValue();
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                var databaseRecord = Server.ServerStore.Cluster.ReadRawDatabaseRecord(context, databaseName);
                foreach (string fld in FieldsThatShouldBeExposedForDebug)
                {
                    if (databaseRecord.Raw.TryGetMember(fld, out var obj))
                    {
                        djv[fld] = obj;
                    }
                }

                using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext jsonContext))
                {
                    using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                    {
                        writer.WriteObject(jsonContext.ReadObject(djv, "databaserecord"));
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}
