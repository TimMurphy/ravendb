﻿namespace Raven.Client.Exceptions.Documents.Revisions
{
    public sealed class RevisionsDisabledException : RavenException
    {
        public RevisionsDisabledException() : base("Revisions are disabled")
        {
        }

        public RevisionsDisabledException(string message) : base(message)
        {
        }
    }
}