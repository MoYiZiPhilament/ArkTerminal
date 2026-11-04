using System.Text;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace ArkTerminal.Rendering;

// Generated from Unicode 16.0 UnicodeData.txt BLOCK OCTANT names.
// https://www.unicode.org/Public/16.0.0/ucd/UnicodeData.txt
internal static class TerminalOctantGridGlyphRenderer
{
    internal static bool TryDraw(ID2D1RenderTarget target, Rune value, Rect rect, ID2D1SolidColorBrush brush)
    {
        if (value.Value is < 0x1CD00 or > 0x1CDE5) return false;
        var mask = GetMask(value.Value);
        if (mask == 0) return false;
        var previousMode = target.AntialiasMode;
        target.AntialiasMode = AntialiasMode.Aliased;
        try
        {
            var cellWidth = rect.Width / 2;
            var cellHeight = rect.Height / 4;
            for (var cell = 0; cell < 8; cell++)
            {
                if ((mask & (1 << cell)) == 0) continue;
                target.FillRectangle(new Rect(
                    rect.X + cell % 2 * cellWidth,
                    rect.Y + cell / 2 * cellHeight,
                    cellWidth,
                    cellHeight), brush);
            }
            return true;
        }
        finally
        {
            target.AntialiasMode = previousMode;
        }
    }

    private static byte GetMask(int codePoint) => codePoint switch
    {
        0x1CD00 => 0x04, // BLOCK OCTANT-3
        0x1CD01 => 0x06, // BLOCK OCTANT-23
        0x1CD02 => 0x07, // BLOCK OCTANT-123
        0x1CD03 => 0x08, // BLOCK OCTANT-4
        0x1CD04 => 0x09, // BLOCK OCTANT-14
        0x1CD05 => 0x0B, // BLOCK OCTANT-124
        0x1CD06 => 0x0C, // BLOCK OCTANT-34
        0x1CD07 => 0x0D, // BLOCK OCTANT-134
        0x1CD08 => 0x0E, // BLOCK OCTANT-234
        0x1CD09 => 0x10, // BLOCK OCTANT-5
        0x1CD0A => 0x11, // BLOCK OCTANT-15
        0x1CD0B => 0x12, // BLOCK OCTANT-25
        0x1CD0C => 0x13, // BLOCK OCTANT-125
        0x1CD0D => 0x15, // BLOCK OCTANT-135
        0x1CD0E => 0x16, // BLOCK OCTANT-235
        0x1CD0F => 0x17, // BLOCK OCTANT-1235
        0x1CD10 => 0x18, // BLOCK OCTANT-45
        0x1CD11 => 0x19, // BLOCK OCTANT-145
        0x1CD12 => 0x1A, // BLOCK OCTANT-245
        0x1CD13 => 0x1B, // BLOCK OCTANT-1245
        0x1CD14 => 0x1C, // BLOCK OCTANT-345
        0x1CD15 => 0x1D, // BLOCK OCTANT-1345
        0x1CD16 => 0x1E, // BLOCK OCTANT-2345
        0x1CD17 => 0x1F, // BLOCK OCTANT-12345
        0x1CD18 => 0x20, // BLOCK OCTANT-6
        0x1CD19 => 0x21, // BLOCK OCTANT-16
        0x1CD1A => 0x22, // BLOCK OCTANT-26
        0x1CD1B => 0x23, // BLOCK OCTANT-126
        0x1CD1C => 0x24, // BLOCK OCTANT-36
        0x1CD1D => 0x25, // BLOCK OCTANT-136
        0x1CD1E => 0x26, // BLOCK OCTANT-236
        0x1CD1F => 0x27, // BLOCK OCTANT-1236
        0x1CD20 => 0x29, // BLOCK OCTANT-146
        0x1CD21 => 0x2A, // BLOCK OCTANT-246
        0x1CD22 => 0x2B, // BLOCK OCTANT-1246
        0x1CD23 => 0x2C, // BLOCK OCTANT-346
        0x1CD24 => 0x2D, // BLOCK OCTANT-1346
        0x1CD25 => 0x2E, // BLOCK OCTANT-2346
        0x1CD26 => 0x2F, // BLOCK OCTANT-12346
        0x1CD27 => 0x30, // BLOCK OCTANT-56
        0x1CD28 => 0x31, // BLOCK OCTANT-156
        0x1CD29 => 0x32, // BLOCK OCTANT-256
        0x1CD2A => 0x33, // BLOCK OCTANT-1256
        0x1CD2B => 0x34, // BLOCK OCTANT-356
        0x1CD2C => 0x35, // BLOCK OCTANT-1356
        0x1CD2D => 0x36, // BLOCK OCTANT-2356
        0x1CD2E => 0x37, // BLOCK OCTANT-12356
        0x1CD2F => 0x38, // BLOCK OCTANT-456
        0x1CD30 => 0x39, // BLOCK OCTANT-1456
        0x1CD31 => 0x3A, // BLOCK OCTANT-2456
        0x1CD32 => 0x3B, // BLOCK OCTANT-12456
        0x1CD33 => 0x3C, // BLOCK OCTANT-3456
        0x1CD34 => 0x3D, // BLOCK OCTANT-13456
        0x1CD35 => 0x3E, // BLOCK OCTANT-23456
        0x1CD36 => 0x41, // BLOCK OCTANT-17
        0x1CD37 => 0x42, // BLOCK OCTANT-27
        0x1CD38 => 0x43, // BLOCK OCTANT-127
        0x1CD39 => 0x44, // BLOCK OCTANT-37
        0x1CD3A => 0x45, // BLOCK OCTANT-137
        0x1CD3B => 0x46, // BLOCK OCTANT-237
        0x1CD3C => 0x47, // BLOCK OCTANT-1237
        0x1CD3D => 0x48, // BLOCK OCTANT-47
        0x1CD3E => 0x49, // BLOCK OCTANT-147
        0x1CD3F => 0x4A, // BLOCK OCTANT-247
        0x1CD40 => 0x4B, // BLOCK OCTANT-1247
        0x1CD41 => 0x4C, // BLOCK OCTANT-347
        0x1CD42 => 0x4D, // BLOCK OCTANT-1347
        0x1CD43 => 0x4E, // BLOCK OCTANT-2347
        0x1CD44 => 0x4F, // BLOCK OCTANT-12347
        0x1CD45 => 0x51, // BLOCK OCTANT-157
        0x1CD46 => 0x52, // BLOCK OCTANT-257
        0x1CD47 => 0x53, // BLOCK OCTANT-1257
        0x1CD48 => 0x54, // BLOCK OCTANT-357
        0x1CD49 => 0x56, // BLOCK OCTANT-2357
        0x1CD4A => 0x57, // BLOCK OCTANT-12357
        0x1CD4B => 0x58, // BLOCK OCTANT-457
        0x1CD4C => 0x59, // BLOCK OCTANT-1457
        0x1CD4D => 0x5B, // BLOCK OCTANT-12457
        0x1CD4E => 0x5C, // BLOCK OCTANT-3457
        0x1CD4F => 0x5D, // BLOCK OCTANT-13457
        0x1CD50 => 0x5E, // BLOCK OCTANT-23457
        0x1CD51 => 0x60, // BLOCK OCTANT-67
        0x1CD52 => 0x61, // BLOCK OCTANT-167
        0x1CD53 => 0x62, // BLOCK OCTANT-267
        0x1CD54 => 0x63, // BLOCK OCTANT-1267
        0x1CD55 => 0x64, // BLOCK OCTANT-367
        0x1CD56 => 0x65, // BLOCK OCTANT-1367
        0x1CD57 => 0x66, // BLOCK OCTANT-2367
        0x1CD58 => 0x67, // BLOCK OCTANT-12367
        0x1CD59 => 0x68, // BLOCK OCTANT-467
        0x1CD5A => 0x69, // BLOCK OCTANT-1467
        0x1CD5B => 0x6A, // BLOCK OCTANT-2467
        0x1CD5C => 0x6B, // BLOCK OCTANT-12467
        0x1CD5D => 0x6C, // BLOCK OCTANT-3467
        0x1CD5E => 0x6D, // BLOCK OCTANT-13467
        0x1CD5F => 0x6E, // BLOCK OCTANT-23467
        0x1CD60 => 0x6F, // BLOCK OCTANT-123467
        0x1CD61 => 0x70, // BLOCK OCTANT-567
        0x1CD62 => 0x71, // BLOCK OCTANT-1567
        0x1CD63 => 0x72, // BLOCK OCTANT-2567
        0x1CD64 => 0x73, // BLOCK OCTANT-12567
        0x1CD65 => 0x74, // BLOCK OCTANT-3567
        0x1CD66 => 0x75, // BLOCK OCTANT-13567
        0x1CD67 => 0x76, // BLOCK OCTANT-23567
        0x1CD68 => 0x77, // BLOCK OCTANT-123567
        0x1CD69 => 0x78, // BLOCK OCTANT-4567
        0x1CD6A => 0x79, // BLOCK OCTANT-14567
        0x1CD6B => 0x7A, // BLOCK OCTANT-24567
        0x1CD6C => 0x7B, // BLOCK OCTANT-124567
        0x1CD6D => 0x7C, // BLOCK OCTANT-34567
        0x1CD6E => 0x7D, // BLOCK OCTANT-134567
        0x1CD6F => 0x7E, // BLOCK OCTANT-234567
        0x1CD70 => 0x7F, // BLOCK OCTANT-1234567
        0x1CD71 => 0x81, // BLOCK OCTANT-18
        0x1CD72 => 0x82, // BLOCK OCTANT-28
        0x1CD73 => 0x83, // BLOCK OCTANT-128
        0x1CD74 => 0x84, // BLOCK OCTANT-38
        0x1CD75 => 0x85, // BLOCK OCTANT-138
        0x1CD76 => 0x86, // BLOCK OCTANT-238
        0x1CD77 => 0x87, // BLOCK OCTANT-1238
        0x1CD78 => 0x88, // BLOCK OCTANT-48
        0x1CD79 => 0x89, // BLOCK OCTANT-148
        0x1CD7A => 0x8A, // BLOCK OCTANT-248
        0x1CD7B => 0x8B, // BLOCK OCTANT-1248
        0x1CD7C => 0x8C, // BLOCK OCTANT-348
        0x1CD7D => 0x8D, // BLOCK OCTANT-1348
        0x1CD7E => 0x8E, // BLOCK OCTANT-2348
        0x1CD7F => 0x8F, // BLOCK OCTANT-12348
        0x1CD80 => 0x90, // BLOCK OCTANT-58
        0x1CD81 => 0x91, // BLOCK OCTANT-158
        0x1CD82 => 0x92, // BLOCK OCTANT-258
        0x1CD83 => 0x93, // BLOCK OCTANT-1258
        0x1CD84 => 0x94, // BLOCK OCTANT-358
        0x1CD85 => 0x95, // BLOCK OCTANT-1358
        0x1CD86 => 0x96, // BLOCK OCTANT-2358
        0x1CD87 => 0x97, // BLOCK OCTANT-12358
        0x1CD88 => 0x98, // BLOCK OCTANT-458
        0x1CD89 => 0x99, // BLOCK OCTANT-1458
        0x1CD8A => 0x9A, // BLOCK OCTANT-2458
        0x1CD8B => 0x9B, // BLOCK OCTANT-12458
        0x1CD8C => 0x9C, // BLOCK OCTANT-3458
        0x1CD8D => 0x9D, // BLOCK OCTANT-13458
        0x1CD8E => 0x9E, // BLOCK OCTANT-23458
        0x1CD8F => 0x9F, // BLOCK OCTANT-123458
        0x1CD90 => 0xA1, // BLOCK OCTANT-168
        0x1CD91 => 0xA2, // BLOCK OCTANT-268
        0x1CD92 => 0xA3, // BLOCK OCTANT-1268
        0x1CD93 => 0xA4, // BLOCK OCTANT-368
        0x1CD94 => 0xA6, // BLOCK OCTANT-2368
        0x1CD95 => 0xA7, // BLOCK OCTANT-12368
        0x1CD96 => 0xA8, // BLOCK OCTANT-468
        0x1CD97 => 0xA9, // BLOCK OCTANT-1468
        0x1CD98 => 0xAB, // BLOCK OCTANT-12468
        0x1CD99 => 0xAC, // BLOCK OCTANT-3468
        0x1CD9A => 0xAD, // BLOCK OCTANT-13468
        0x1CD9B => 0xAE, // BLOCK OCTANT-23468
        0x1CD9C => 0xB0, // BLOCK OCTANT-568
        0x1CD9D => 0xB1, // BLOCK OCTANT-1568
        0x1CD9E => 0xB2, // BLOCK OCTANT-2568
        0x1CD9F => 0xB3, // BLOCK OCTANT-12568
        0x1CDA0 => 0xB4, // BLOCK OCTANT-3568
        0x1CDA1 => 0xB5, // BLOCK OCTANT-13568
        0x1CDA2 => 0xB6, // BLOCK OCTANT-23568
        0x1CDA3 => 0xB7, // BLOCK OCTANT-123568
        0x1CDA4 => 0xB8, // BLOCK OCTANT-4568
        0x1CDA5 => 0xB9, // BLOCK OCTANT-14568
        0x1CDA6 => 0xBA, // BLOCK OCTANT-24568
        0x1CDA7 => 0xBB, // BLOCK OCTANT-124568
        0x1CDA8 => 0xBC, // BLOCK OCTANT-34568
        0x1CDA9 => 0xBD, // BLOCK OCTANT-134568
        0x1CDAA => 0xBE, // BLOCK OCTANT-234568
        0x1CDAB => 0xBF, // BLOCK OCTANT-1234568
        0x1CDAC => 0xC1, // BLOCK OCTANT-178
        0x1CDAD => 0xC2, // BLOCK OCTANT-278
        0x1CDAE => 0xC3, // BLOCK OCTANT-1278
        0x1CDAF => 0xC4, // BLOCK OCTANT-378
        0x1CDB0 => 0xC5, // BLOCK OCTANT-1378
        0x1CDB1 => 0xC6, // BLOCK OCTANT-2378
        0x1CDB2 => 0xC7, // BLOCK OCTANT-12378
        0x1CDB3 => 0xC8, // BLOCK OCTANT-478
        0x1CDB4 => 0xC9, // BLOCK OCTANT-1478
        0x1CDB5 => 0xCA, // BLOCK OCTANT-2478
        0x1CDB6 => 0xCB, // BLOCK OCTANT-12478
        0x1CDB7 => 0xCC, // BLOCK OCTANT-3478
        0x1CDB8 => 0xCD, // BLOCK OCTANT-13478
        0x1CDB9 => 0xCE, // BLOCK OCTANT-23478
        0x1CDBA => 0xCF, // BLOCK OCTANT-123478
        0x1CDBB => 0xD0, // BLOCK OCTANT-578
        0x1CDBC => 0xD1, // BLOCK OCTANT-1578
        0x1CDBD => 0xD2, // BLOCK OCTANT-2578
        0x1CDBE => 0xD3, // BLOCK OCTANT-12578
        0x1CDBF => 0xD4, // BLOCK OCTANT-3578
        0x1CDC0 => 0xD5, // BLOCK OCTANT-13578
        0x1CDC1 => 0xD6, // BLOCK OCTANT-23578
        0x1CDC2 => 0xD7, // BLOCK OCTANT-123578
        0x1CDC3 => 0xD8, // BLOCK OCTANT-4578
        0x1CDC4 => 0xD9, // BLOCK OCTANT-14578
        0x1CDC5 => 0xDA, // BLOCK OCTANT-24578
        0x1CDC6 => 0xDB, // BLOCK OCTANT-124578
        0x1CDC7 => 0xDC, // BLOCK OCTANT-34578
        0x1CDC8 => 0xDD, // BLOCK OCTANT-134578
        0x1CDC9 => 0xDE, // BLOCK OCTANT-234578
        0x1CDCA => 0xDF, // BLOCK OCTANT-1234578
        0x1CDCB => 0xE0, // BLOCK OCTANT-678
        0x1CDCC => 0xE1, // BLOCK OCTANT-1678
        0x1CDCD => 0xE2, // BLOCK OCTANT-2678
        0x1CDCE => 0xE3, // BLOCK OCTANT-12678
        0x1CDCF => 0xE4, // BLOCK OCTANT-3678
        0x1CDD0 => 0xE5, // BLOCK OCTANT-13678
        0x1CDD1 => 0xE6, // BLOCK OCTANT-23678
        0x1CDD2 => 0xE7, // BLOCK OCTANT-123678
        0x1CDD3 => 0xE8, // BLOCK OCTANT-4678
        0x1CDD4 => 0xE9, // BLOCK OCTANT-14678
        0x1CDD5 => 0xEA, // BLOCK OCTANT-24678
        0x1CDD6 => 0xEB, // BLOCK OCTANT-124678
        0x1CDD7 => 0xEC, // BLOCK OCTANT-34678
        0x1CDD8 => 0xED, // BLOCK OCTANT-134678
        0x1CDD9 => 0xEE, // BLOCK OCTANT-234678
        0x1CDDA => 0xEF, // BLOCK OCTANT-1234678
        0x1CDDB => 0xF1, // BLOCK OCTANT-15678
        0x1CDDC => 0xF2, // BLOCK OCTANT-25678
        0x1CDDD => 0xF3, // BLOCK OCTANT-125678
        0x1CDDE => 0xF4, // BLOCK OCTANT-35678
        0x1CDDF => 0xF6, // BLOCK OCTANT-235678
        0x1CDE0 => 0xF7, // BLOCK OCTANT-1235678
        0x1CDE1 => 0xF8, // BLOCK OCTANT-45678
        0x1CDE2 => 0xF9, // BLOCK OCTANT-145678
        0x1CDE3 => 0xFB, // BLOCK OCTANT-1245678
        0x1CDE4 => 0xFD, // BLOCK OCTANT-1345678
        0x1CDE5 => 0xFE, // BLOCK OCTANT-2345678
        _ => 0
    };
}
