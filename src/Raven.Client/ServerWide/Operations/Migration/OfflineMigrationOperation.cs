﻿using System;
using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Json.Serialization;
using Sparrow.Json;

namespace Raven.Client.ServerWide.Operations.Migration
{
    public sealed class OfflineMigrationOperation : IServerOperation<OperationIdResult>
    {
        private readonly OfflineMigrationConfiguration _configuration;

        public OfflineMigrationOperation(OfflineMigrationConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _configuration.Validate();
        }

        public RavenCommand<OperationIdResult> GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new OfflineMigrationCommand(conventions, _configuration);
        }

        private sealed class OfflineMigrationCommand : RavenCommand<OperationIdResult>
        {
            private readonly DocumentConventions _conventions;
            private readonly OfflineMigrationConfiguration _configuration;

            public OfflineMigrationCommand(DocumentConventions conventions, OfflineMigrationConfiguration configuration)
            {
                _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
                _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            }

            public override bool IsReadRequest => false;

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/admin/migrate/offline";

                return new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    Content = new BlittableJsonContent(async stream =>
                    {
                        var config = DocumentConventions.Default.Serialization.DefaultConverter.ToBlittable(_configuration, ctx);
                        await ctx.WriteAsync(stream, config).ConfigureAwait(false);
                    }, _conventions)
                };
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.OperationIdResult(response);
            }
        }
    }
}
