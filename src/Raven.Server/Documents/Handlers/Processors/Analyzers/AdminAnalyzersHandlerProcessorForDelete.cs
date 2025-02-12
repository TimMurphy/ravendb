﻿using JetBrains.Annotations;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.Analyzers
{
    internal sealed class AdminAnalyzersHandlerProcessorForDelete : AbstractAdminAnalyzersHandlerProcessorForDelete<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public AdminAnalyzersHandlerProcessorForDelete([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }
    }
}
