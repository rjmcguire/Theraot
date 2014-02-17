﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Theraot.Core
{
    public static class CancellationTokenHelper
    {
        public static void ThrowIfSourceDisposed(CancellationToken cancellationToken)
        {
            //CancellationToken.WaitHandle is documented to throw ObjectDispodesException is the source of the CancellationToken is disposed.
            GC.KeepAlive(cancellationToken.WaitHandle);
        }
    }
}