﻿// -----------------------------------------------------------------------
//  <copyright file="ConnectionOptions.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Net;
using Raven.Abstractions.Extensions;

namespace Raven.Client.Connection
{
	public class ConnectionOptions
	{
		public static IDisposable Expect100Continue(Uri uri)
		{
#if SILVERLIGHT || NETFX_CORE
			return new DisposableAction(() => { });
#else
#pragma warning disable SYSLIB0014 // Type or member is obsolete
            var servicePoint = ServicePointManager.FindServicePoint(uri);
#pragma warning restore SYSLIB0014 // Type or member is obsolete
            servicePoint.Expect100Continue = true;
			return new DisposableAction(() => servicePoint.Expect100Continue = false);
#endif
		}
    }
}