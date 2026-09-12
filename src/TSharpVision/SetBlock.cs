using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TSharpVision;

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

/// <summary>State-change request pairing a view-state mask with whether its bits should be enabled.</summary>
/// <param name="st">View-state bits to update.</param>
/// <param name="en">True to enable the bits; false to clear them.</param>
public readonly record struct SetBlock(ushort st, bool en);