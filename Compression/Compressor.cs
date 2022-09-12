﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AMP.Compression {
    internal interface Compressor {

        byte[] Compress(byte[] data);
        byte[] Decompress(byte[] data);

    }
}
