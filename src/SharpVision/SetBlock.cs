using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpVision;

//public struct SetBlock
//{
//    public ushort st;
//    public bool en;

//    public SetBlock(ushort st, bool en)
//    {
//        this.st = st;
//        this.en = en;
//    }
//}

public readonly record struct SetBlock(ushort st, bool en);