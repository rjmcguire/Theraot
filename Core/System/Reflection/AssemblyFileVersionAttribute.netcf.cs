﻿#if NETCF

using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace System.Reflection
{
    [AttributeUsageAttribute(AttributeTargets.Assembly, Inherited = false)]
    [ComVisibleAttribute(true)]
    public sealed class AssemblyFileVersionAttribute : Attribute
    {
        private string _version;

        public AssemblyFileVersionAttribute(string version)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }
            _version = version;
        }

        public string Version
        {
            get
            {
                return _version;
            }
        }
    }
}

#endif