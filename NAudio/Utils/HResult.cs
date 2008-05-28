﻿using System;
using System.Collections.Generic;
using System.Text;

namespace NAudio.Utils
{
    public static class HResult
    {
        public static int MAKE_HRESULT(int sev, int fac, int code)
        {
            return (int) (((uint)sev) << 31 | ((uint)fac) << 16 | ((uint)code));
        }

        const int FACILITY_AAF = 18;
        const int FACILITY_ACS = 20;
        const int FACILITY_BACKGROUNDCOPY = 32;
        const int FACILITY_CERT = 11;  
        const int FACILITY_COMPLUS =          17;  
        const int FACILITY_CONFIGURATION =    33;  
        const int FACILITY_CONTROL          = 10;  
        const int FACILITY_DISPATCH          = 2;  
        const int FACILITY_DPLAY            = 21;  
        const int FACILITY_HTTP            =  25;  
        const int FACILITY_INTERNET         = 12;  
        const int FACILITY_ITF              =  4;  
        const int FACILITY_MEDIASERVER      = 13;  
        const int FACILITY_MSMQ             = 14;  
        const int FACILITY_NULL             =  0;  
        const int FACILITY_RPC              =  1;  
        const int FACILITY_SCARD            = 16;  
        const int FACILITY_SECURITY         =  9;  
        const int FACILITY_SETUPAPI         = 15;  
        const int FACILITY_SSPI             =  9;  
        const int FACILITY_STORAGE          =  3;  
        const int FACILITY_SXS              = 23;  
        const int FACILITY_UMI              = 22;  
        const int FACILITY_URT              = 19;  
        const int FACILITY_WIN32            =  7;  
        const int FACILITY_WINDOWS          =  8;  
        const int FACILITY_WINDOWS_CE       = 24; 
    }

}