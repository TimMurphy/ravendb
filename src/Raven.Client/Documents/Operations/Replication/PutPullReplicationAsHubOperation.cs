﻿using System;
using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.OngoingTasks;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Json.Serialization;
using Raven.Client.Util;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.Replication
{
    public sealed class PutPullReplicationAsHubOperation : IMaintenanceOperation<ModifyOngoingTaskResult>
    {
        private readonly PullReplicationDefinition _pullReplicationDefinition;

        public PutPullReplicationAsHubOperation(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException($"'{nameof(name)}' must have value");
            }

            _pullReplicationDefinition = new PullReplicationDefinition(name);
        }

        public PutPullReplicationAsHubOperation(PullReplicationDefinition pullReplicationDefinition)
        {
            if (string.IsNullOrEmpty(pullReplicationDefinition.Name))
            {
                throw new ArgumentException($"'{nameof(pullReplicationDefinition.Name)}' must have value");
            }
            _pullReplicationDefinition = pullReplicationDefinition;
        }

        public RavenCommand<ModifyOngoingTaskResult> GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new UpdatePullReplicationDefinitionCommand(conventions, _pullReplicationDefinition);
        }

        private sealed class UpdatePullReplicationDefinitionCommand : RavenCommand<ModifyOngoingTaskResult>, IRaftCommand
        {
            private readonly DocumentConventions _conventions;
            private readonly PullReplicationDefinition _pullReplicationDefinition;

            public UpdatePullReplicationDefinitionCommand(DocumentConventions conventions, PullReplicationDefinition pullReplicationDefinition)
            {
                _conventions = conventions;
                _pullReplicationDefinition = pullReplicationDefinition;
            }

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/admin/tasks/pull-replication/hub";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    Content = new BlittableJsonContent(async stream => await ctx.WriteAsync(stream, ctx.ReadObject(_pullReplicationDefinition.ToJson(), "update-pull-replication-definition")).ConfigureAwait(false), _conventions)
                };

                return request;
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.ModifyOngoingTaskResult(response);
            }

            public override bool IsReadRequest => false;
            public string RaftUniqueRequestId { get; } = RaftIdGenerator.NewId();
        }
    }
}
