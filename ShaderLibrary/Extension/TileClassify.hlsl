#ifndef UNIVERSAL_CLASSIFY_INCLUDED
#define UNIVERSAL_CLASSIFY_INCLUDED

#define TILE_INDEX_MASK (32767)
#define TILE_INDEX_SHIFT_X (0)
#define TILE_INDEX_SHIFT_Y (15)
#define TILE_INDEX_SHIFT_EYE (30)



uint2 DecodeTileIndex(uint encoded)
{
    return uint2((encoded >> TILE_INDEX_SHIFT_X) & TILE_INDEX_MASK, (encoded >> TILE_INDEX_SHIFT_Y) & TILE_INDEX_MASK);
}

uint EncodeTileIndex(uint2 tileID)
{
    return (unity_StereoEyeIndex << TILE_INDEX_SHIFT_EYE) | (tileID.y << TILE_INDEX_SHIFT_Y) | (tileID.x << TILE_INDEX_SHIFT_X);
}





uint PackTileCoord(uint2 coord)
{
    return (coord.x << 16u) | coord.y;
}

uint2 UnpackTileCoord(uint tile)
{
    uint pos = tile;
    return uint2((pos >> 16u) & 0xffff, pos & 0xffff);
}
#endif
