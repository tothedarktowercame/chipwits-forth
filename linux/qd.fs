\ qd.fs -- QuickDraw-subset renderer for the ChipWits+ native port.
\ Real 1-bit bitmap graphics: pixels, patterns, rect verbs, lines, ovals,
\ CopyBits with the four transfer modes, and 8x8 bitmap-font text.
\ Included by macforth-shim.fs after the window section (needs cur-bmap,
\ the-screen, rects, white/black/gray patterns).

decimal
include font8x8.fs

\ ---------- BitMap accessors: baseAddr(4) rowBytes(2) bounds t l b r ----------
: bm-base ( bm -- a ) @ ;
: bm-rb   ( bm -- n ) 4 + w@ ;
: bm-t 6 + w@ ;   : bm-l 8 + w@ ;
: bm-b 10 + w@ ;  : bm-r 12 + w@ ;

\ ---------- pen / text state ----------
variable pen-x   variable pen-y
variable pen-w   variable pen-h
variable pen-mode  variable pen-pat  variable back-pat
variable text-mode variable text-size
variable gfx-text  \ 0 = type/emit go to console only (test harness reports)
: +gfx -1 gfx-text ! ;   : -gfx 0 gfx-text ! ;
: pensize ( w h -- ) pen-h ! pen-w ! ;
: penmode ( m -- ) pen-mode ! ;
: penpat  ( pat -- ) pen-pat ! ;
: backpat ( pat -- ) back-pat ! ;
: textsize ( n -- ) text-size ! ;
: textmode ( m -- ) text-mode ! ;
: @pen ( -- x y ) pen-x @ pen-y @ ;
: move.to ( x y -- ) pen-y ! pen-x ! ;
: rmove ( dx dy -- ) pen-y +! pen-x +! ;
: ginit 1 1 pensize 8 penmode black penpat white backpat ;
ginit  1 text-mode !  12 text-size !  +gfx

\ ---------- pixel core (bitmap-local coords = global - bounds origin) ----------
: px! { v x y bm | lx ly a m -- }
   bm 0= if exit then
   x bm bm-l - -> lx   y bm bm-t - -> ly
   lx 0< ly 0< or if exit then
   lx bm bm-r bm bm-l - < not if exit then
   ly bm bm-b bm bm-t - < not if exit then
   bm bm-base ly bm bm-rb * + lx 3 rshift + -> a
   128 lx 7 and rshift -> m
   v if a c@ m or else a c@ m invert and then a c! ;

: px@ { x y bm | lx ly -- v }
   bm 0= if 0 exit then
   x bm bm-l - -> lx   y bm bm-t - -> ly
   lx 0< ly 0< or if 0 exit then
   lx bm bm-r bm bm-l - < not if 0 exit then
   ly bm bm-b bm bm-t - < not if 0 exit then
   bm bm-base ly bm bm-rb * + lx 3 rshift + c@
   128 lx 7 and rshift and 0<> 1 and ;

\ transfer modes: 0/8 copy, 1/9 or, 2/10 xor, 3/11 bic
: blit-px { v x y bm mode -- }
   mode 3 and case
     0 of v x y bm px! endof
     1 of v if 1 x y bm px! then endof
     2 of v if x y bm px@ 1 xor x y bm px! then endof
     3 of v if 0 x y bm px! then endof
   endcase ;

: pat-bit { x y pat -- v }
   pat y 7 and + c@ 128 x 7 and rshift and 0<> 1 and ;

\ fill [xa,xb) x [ya,yb) with pattern through mode
: (fill) { xa ya xb yb pat mode bm -- }
   yb ya ?do xb xa ?do
     i j pat pat-bit  i j bm mode blit-px
   loop loop ;

\ ---------- rect / oval / line verbs ----------
: minmax ( a b -- min max ) 2dup > if swap then ;

: rectangle { x1 y1 x2 y2 mode | xa ya xb yb -- }
   x1 x2 minmax -> xb -> xa
   y1 y2 minmax -> yb -> ya
   mode case
     0 of \ frame: pen-thick bands just inside the rect
       xa ya xb ya pen-h @ + yb min pen-pat @ pen-mode @ cur-bmap @ (fill)
       xa yb pen-h @ - ya max xb yb pen-pat @ pen-mode @ cur-bmap @ (fill)
       xa ya xa pen-w @ + xb min yb pen-pat @ pen-mode @ cur-bmap @ (fill)
       xb pen-w @ - xa max ya xb yb pen-pat @ pen-mode @ cur-bmap @ (fill)
     endof
     1 of xa ya xb yb pen-pat @ pen-mode @ cur-bmap @ (fill) endof
     2 of xa ya xb yb back-pat @ 8 cur-bmap @ (fill) endof
     3 of xa ya xb yb black 10 cur-bmap @ (fill) endof
   endcase ;

: rrectangle { x1 y1 x2 y2 ow oh mode -- } x1 y1 x2 y2 mode rectangle ;

\ oval: integer inside-ellipse test; ChipWits ovals are small (lozenges)
: in-oval? { x y xa ya xb yb | w h dx dy -- f }
   xb xa - -> w   yb ya - -> h
   x 2* xa xb + 1- - -> dx   y 2* ya yb + 1- - -> dy
   dx dx * h h * *  dy dy * w w * *  +  w w * h h * *  > not ;
: oval { x1 y1 x2 y2 mode | xa ya xb yb v -- }
   x1 x2 minmax -> xb -> xa   y1 y2 minmax -> yb -> ya
   xb xa - yb ya - * 40000 > if xa ya xb yb mode rectangle exit then
   yb ya ?do xb xa ?do
     i j xa ya xb yb in-oval? if
       mode case
         0 of i j xa 1+ ya 1+ xb 1- yb 1- in-oval? not if
                1 i j cur-bmap @ pen-mode @ blit-px then endof
         1 of i j pen-pat @ pat-bit i j cur-bmap @ pen-mode @ blit-px endof
         2 of i j back-pat @ pat-bit i j cur-bmap @ 8 blit-px endof
         3 of 1 i j cur-bmap @ 10 blit-px endof
       endcase
     then
   loop loop ;

: pen-stamp { x y -- }
   x y x pen-w @ + y pen-h @ + pen-pat @ pen-mode @ cur-bmap @ (fill) ;
: vector { x1 y1 x2 y2 | dx dy sx sy err -- }
   x2 x1 - abs -> dx   y2 y1 - abs negate -> dy
   x1 x2 < if 1 else -1 then -> sx
   y1 y2 < if 1 else -1 then -> sy
   dx dy + -> err
   begin
     x1 y1 pen-stamp
     x1 x2 = y1 y2 = and if exit then
     err 2* dup dy < not if dy err + -> err x1 sx + -> x1 then
     dx < if dx err + -> err y1 sy + -> y1 then
   again ;
: draw.to ( x y -- ) 2dup @pen 2swap vector move.to ;
: rdraw ( dx dy -- ) swap pen-x @ + swap pen-y @ + draw.to ;

\ ---------- CopyBits: equal-size blit, rects in each bitmap's bounds space ----------
: (copybits) { src dst sr dr mode rgn | st sl dt dl w h -- }
   sr w@ -> st   sr 2+ w@ -> sl
   dr w@ -> dt   dr 2+ w@ -> dl
   sr 6 + w@ sl - -> w   sr 4 + w@ st - -> h
   h 0 ?do w 0 ?do
     sl i + st j + src px@
     dl i + dt j + dst mode 3 and blit-px
   loop loop ;

\ ---------- text: 8x8 font, scaled by textsize (12->1x, 24->2x, 36->3x) ----------
: gscale ( -- s ) text-size @ dup 16 < if drop 1 else 28 < if 2 else 3 then then ;
: gemit { c | g s x0 y0 v -- }
   gfx-text @ 0= if exit then
   c 127 and 8 * font8x8 + -> g
   gscale -> s
   pen-x @ -> x0   pen-y @ 7 s * - -> y0
   8 0 do 8 0 do
     g j + c@ 128 i rshift and 0<> 1 and -> v
     v text-mode @ 3 and 0= or if
       x0 i s * +  y0 j s * +  over s +  over s +
       2swap 2swap v if black else white then 8 cur-bmap @ (fill)
     then
   loop loop
   8 s * pen-x +! ;
: stringwidth ( c$ -- w ) count nip 8 * gscale * ;

\ console words also draw at the pen (MacForth console = the screen)
: ctype type ;
: cemit emit ;
: type ( a u -- ) 2dup ctype over + swap ?do i c@ gemit loop ;
: emit ( c -- ) dup cemit gemit ;
: space 32 emit ;
: spaces 0 max 0 ?do space loop ;
: (num$) ( n -- a u ) dup abs 0 <# #s rot sign #> ;
: . (num$) type space ;
: .r { n w -- } n (num$) w over - spaces type ;
: u. 0 <# #s #> type space ;
: ." state @ if postpone s" postpone type
   else [char] " parse type then ; immediate

\ ---------- screenshots: portable bitmap (P4) ----------
create nl-buf 10 c,
: save-pbm { nadr nlen | fid -- }
   nadr nlen w/o create-file abort" cannot create pbm" -> fid
   s" P4" fid write-file drop  nl-buf 1 fid write-file drop
   s" 512 342" fid write-file drop  nl-buf 1 fid write-file drop
   the-screen 512 8 / 342 * fid write-file drop
   fid close-file drop ;
: save-screen s" screen.pbm" save-pbm ;
