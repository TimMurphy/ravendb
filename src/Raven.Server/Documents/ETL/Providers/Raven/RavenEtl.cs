﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using Raven.Client.Documents.Commands.Batches;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Server.Documents.ETL.Providers.Raven.Enumerators;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.ETL.Providers.Raven
{
    public class RavenEtl : EtlProcess<RavenEtlItem, ICommandData>
    {
        public const string RavenEtlTag = "Raven ETL";

        public RavenEtl(DocumentDatabase database, RavenEtlConfiguration configuration) : base(database, configuration, RavenEtlTag)
        {
            EtlConfiguration = configuration;
        }

        public RavenEtlConfiguration EtlConfiguration { get; }

        protected override IEnumerator<RavenEtlItem> ConvertDocsEnumerator(IEnumerator<Document> docs)
        {
            return new DocumentsToRavenEtlItems(docs);
        }

        protected override IEnumerator<RavenEtlItem> ConvertTombstonesEnumerator(IEnumerator<DocumentTombstone> tombstones)
        {
            return new TombstonesToRavenEtlItems(tombstones);
        }

        protected override EtlTransformer<RavenEtlItem, ICommandData> GetTransformer(DocumentsOperationContext context)
        {
            return new RavenEtlDocumentTransformer(Database, context, EtlConfiguration);
        }

        protected override void LoadInternal(IEnumerable<ICommandData> commands, JsonOperationContext context)
        {
            using (var requestExecutor = RequestExecutor.CreateForSingleNode(EtlConfiguration.Url, EtlConfiguration.Database, EtlConfiguration.ApiKey)) // TODO arek - consider caching it somewhere
            {
                var batchCommand = new BatchCommand(new DocumentConventions(), context, commands as List<ICommandData>);

                requestExecutor.Execute(batchCommand, context);
            }
        }

        public override bool CanContinueBatch()
        {
            return true; // TODO 
        }

        protected override void UpdateMetrics(DateTime startTime, Stopwatch duration, int batchSize)
        {
            // TODO arek
        }
    }
}