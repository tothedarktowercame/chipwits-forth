/* pfcustom.c -- ChipWits+ native port: C cores for the QuickDraw renderer.
**
** Replaces pforth's stock csrc/pfcustom.c (setup.sh copies it in before make).
** CBLIT/CFILLPAT each take one parameter-block address from the stack;
** all fields are 32-bit cells.  Bits are MSB = leftmost pixel, 1 = black.
** CMSEC ( -- ms ) is a monotonic millisecond clock (wraps; compare by
** difference) -- the sound queue's timekeeper.
**
** CBLIT block:  0 srcBase 1 srcRowBytes 2 sx 3 sy   (src local top-left)
**               4 dstBase 5 dstRowBytes 6 dx 7 dy   (dst local top-left)
**               8 w 9 h 10 op(0 copy,1 or,2 xor,3 bic)
**               11 srcW 12 srcH 13 dstW 14 dstH     (full sizes, for clip)
**
** CFILLPAT block: 0 base 1 rowBytes 2 boundsL 3 boundsT 4 fullW 5 fullH
**                 6 xa 7 ya 8 xb 9 yb  (global coords, half-open)
**                 10 patAddr (8 bytes) 11 op
*/

#ifndef PF_USER_CUSTOM

#include "pf_all.h"
#include <stdint.h>
#include <time.h>

static void CBlit( cell_t param );
static void CFillPat( cell_t param );

static cell_t CMsec( void )
{
    struct timespec ts;
    clock_gettime( CLOCK_MONOTONIC, &ts );
    return (cell_t) (uint32_t) (ts.tv_sec * 1000u + ts.tv_nsec / 1000000);
}

static void CBlit( cell_t param )
{
    cell_t *q = (cell_t *) param;
    uint8_t *sb = (uint8_t *) q[0];
    cell_t srb = q[1], sx = q[2], sy = q[3];
    uint8_t *db = (uint8_t *) q[4];
    cell_t drb = q[5], dx = q[6], dy = q[7];
    cell_t w = q[8], h = q[9], op = q[10] & 3;
    cell_t sw = q[11], sh = q[12], dw = q[13], dh = q[14];
    cell_t i, j;
    for( j = 0; j < h; j++ )
    {
        cell_t syy = sy + j, dyy = dy + j;
        if( dyy < 0 || dyy >= dh ) continue;
        for( i = 0; i < w; i++ )
        {
            cell_t sxx = sx + i, dxx = dx + i;
            int bit = 0;
            uint8_t *d, m;
            if( dxx < 0 || dxx >= dw ) continue;
            if( sxx >= 0 && syy >= 0 && sxx < sw && syy < sh )
                bit = ( sb[ syy * srb + ( sxx >> 3 ) ] >> ( 7 - ( sxx & 7 ) ) ) & 1;
            d = &db[ dyy * drb + ( dxx >> 3 ) ];
            m = (uint8_t)( 0x80u >> ( dxx & 7 ) );
            switch( op )
            {
            case 0: if( bit ) *d |= m; else *d &= (uint8_t)~m; break;
            case 1: if( bit ) *d |= m; break;
            case 2: if( bit ) *d ^= m; break;
            case 3: if( bit ) *d &= (uint8_t)~m; break;
            }
        }
    }
}

static void CFillPat( cell_t param )
{
    cell_t *q = (cell_t *) param;
    uint8_t *base = (uint8_t *) q[0];
    cell_t rb = q[1], bl = q[2], bt = q[3], fw = q[4], fh = q[5];
    cell_t xa = q[6], ya = q[7], xb = q[8], yb = q[9];
    uint8_t *pat = (uint8_t *) q[10];
    cell_t op = q[11] & 3;
    cell_t x, y;
    for( y = ya; y < yb; y++ )
    {
        cell_t ly = y - bt;
        uint8_t prow;
        if( ly < 0 || ly >= fh ) continue;
        prow = pat[ y & 7 ];
        for( x = xa; x < xb; x++ )
        {
            cell_t lx = x - bl;
            int bit;
            uint8_t *d, m;
            if( lx < 0 || lx >= fw ) continue;
            bit = ( prow >> ( 7 - ( x & 7 ) ) ) & 1;
            d = &base[ ly * rb + ( lx >> 3 ) ];
            m = (uint8_t)( 0x80u >> ( lx & 7 ) );
            switch( op )
            {
            case 0: if( bit ) *d |= m; else *d &= (uint8_t)~m; break;
            case 1: if( bit ) *d |= m; break;
            case 2: if( bit ) *d ^= m; break;
            case 3: if( bit ) *d &= (uint8_t)~m; break;
            }
        }
    }
}

#ifdef PF_NO_GLOBAL_INIT
#define NUM_CUSTOM_FUNCTIONS  (3)
CFunc0 CustomFunctionTable[NUM_CUSTOM_FUNCTIONS];
Err LoadCustomFunctionTable( void )
{
    CustomFunctionTable[0] = (CFunc0) CBlit;
    CustomFunctionTable[1] = (CFunc0) CFillPat;
    CustomFunctionTable[2] = (CFunc0) CMsec;
    return 0;
}
#else
CFunc0 CustomFunctionTable[] =
{
    (CFunc0) CBlit,
    (CFunc0) CFillPat,
    (CFunc0) CMsec
};
#endif

#if (!defined(PF_NO_INIT)) && (!defined(PF_NO_SHELL))
Err CompileCustomFunctions( void )
{
    Err err;
    int i = 0;
    err = CreateGlueToC( "CBLIT", i++, C_RETURNS_VOID, 1 );
    if( err < 0 ) return err;
    err = CreateGlueToC( "CFILLPAT", i++, C_RETURNS_VOID, 1 );
    if( err < 0 ) return err;
    err = CreateGlueToC( "CMSEC", i++, C_RETURNS_VALUE, 0 );
    if( err < 0 ) return err;
    return 0;
}
#else
Err CompileCustomFunctions( void ) { return 0; }
#endif

#endif  /* PF_USER_CUSTOM */
