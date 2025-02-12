using System;

namespace Raven.Client.Documents.Indexes
{
    /// <summary>
    /// When applied to a custom extension method used in an index, RavenDB will translate
    /// the invocation to a regular method call so it can be understood by the server.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RavenMethodAttribute : Attribute
    {
    }
}
