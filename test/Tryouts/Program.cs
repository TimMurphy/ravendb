﻿using System;
using FastTests.Server.Documents.Patching;
using FastTests.Server.Documents.Replication;
using Sparrow.Logging;

namespace Tryouts
{
    public class Program
    {
        static unsafe void Main(string[] args)
        {
            //LoggingSource.Instance.SetupLogMode(LogMode.Information, "E:\\Work");
            for (int i = 0; i < 1000; i++)
            {
                Console.WriteLine(i);
                using (var store = new FastTests.Server.Basic.ServerStore())
                {
                    store.Admin_databases_endpoint_should_refuse_document_with_lower_etag_with_concurrency_Exception();
                }
            }
        }
    }

}

