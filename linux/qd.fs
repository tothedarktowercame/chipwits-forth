\ qd.fs -- QuickDraw-subset renderer for the ChipWits+ native port.
\ Real 1-bit bitmap graphics: pixels, patterns, rect verbs, lines, ovals,
\ CopyBits with the four transfer modes, PICTs, and the Mac bitmap fonts.
\ Included by macforth-shim.fs after the window section (needs cur-bmap,
\ the-screen, rects, white/black/gray patterns).

decimal

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

\ fill [xa,xb) x [ya,yb) with pattern through mode -- C core (CFILLPAT)
create fp( 12 cells allot
: (fill) { xa ya xb yb pat mode bm -- }
   bm 0= if exit then
   bm bm-base fp( !          bm bm-rb fp( 4 + !
   bm bm-l fp( 8 + !         bm bm-t fp( 12 + !
   bm bm-r bm bm-l - fp( 16 + !   bm bm-b bm bm-t - fp( 20 + !
   xa fp( 24 + !  ya fp( 28 + !  xb fp( 32 + !  yb fp( 36 + !
   pat fp( 40 + !  mode fp( 44 + !
   fp( cfillpat ;

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
\ C core (CBLIT); rect coords are translated to bitmap-local before the call.
create cb( 15 cells allot
: (copybits) { src dst sr dr mode rgn -- }
   src 0= dst 0= or if exit then
   src bm-base cb( !          src bm-rb cb( 4 + !
   sr 2+ w@ src bm-l - cb( 8 + !    sr w@ src bm-t - cb( 12 + !
   dst bm-base cb( 16 + !     dst bm-rb cb( 20 + !
   dr 2+ w@ dst bm-l - cb( 24 + !   dr w@ dst bm-t - cb( 28 + !
   sr 6 + w@ sr 2+ w@ - cb( 32 + !  sr 4 + w@ sr w@ - cb( 36 + !
   mode cb( 40 + !
   src bm-r src bm-l - cb( 44 + !   src bm-b src bm-t - cb( 48 + !
   dst bm-r dst bm-l - cb( 52 + !   dst bm-b dst bm-t - cb( 56 + !
   cb( cblit ;

\ ---------- DrawPicture: pre-rendered PICTs (see get.picture in the shim) ----------
\ The picture's frame maps onto the destination rect; equal sizes go
\ through CopyBits, anything else is scaled nearest-neighbour.
create pic-bm 14 allot
: (drawpicture) { h r | p pw ph dw dh -- }
   h 0= if exit then  h @ -> p
   p 12 + pic-bm !  p 8 + w@ pic-bm 4 + w!  p pic-bm 6 + 8 cmove
   p 6 + w@ p 2+ w@ - -> pw   p 4 + w@ p w@ - -> ph
   r 6 + w@ r 2+ w@ - -> dw   r 4 + w@ r w@ - -> dh
   pw dw = ph dh = and if
     pic-bm cur-bmap @ pic-bm 6 + r srccopy 0 (copybits) exit
   then
   dw 1 < dh 1 < or if exit then
   dh 0 do dw 0 do
     p 2+ w@ i pw * dw / +  p w@ j ph * dh / +  pic-bm px@
     r 2+ w@ i +  r w@ j +  cur-bmap @ px!
   loop loop ;

\ ---------- text: the Mac's own bitmap fonts ----------
\ tools/extract_resources.py converts the FONT resources from the disk
\ image to data/font-NNN.bin (NNN = family*128 + size; layout documented
\ there).  Chicago 12 is the system font (textfont 0), which is what the
\ game draws in.  Other sizes scale the nearest strike, as QuickDraw does.
variable text-font  variable text-face
: textfont ( n -- ) text-font ! ;
: textstyle ( n -- ) text-face ! ;    \ 1 bold, 8 outline (others ignored)
: sw@ ( a -- n ) w@ dup 32767 > if 65536 - then ;

create path-buf 32 allot
: path+ ( len a u -- len' ) >r over path-buf + r@ cmove r> + ;
: data-path ( id a u -- a u )   \ "data/<a u><id>.bin"
   0 s" data/" path+ -rot path+  swap 0 <# #s #> path+  s" .bin" path+
   path-buf swap ;

create fonts 8 2* cells allot   \ [id][record] pairs
variable nfonts
: load-font { id | fid len a -- }
   id s" font-" data-path r/o open-file
   abort" missing data/font-*.bin -- run ./setup.sh" -> fid
   fid file-size 2drop -> len  len allocate drop -> a
   a len fid read-file 2drop  fid close-file drop
   id  fonts nfonts @ 2* cells + !  a  fonts nfonts @ 2* cells + cell+ !
   1 nfonts +! ;
12 load-font  393 load-font  396 load-font  521 load-font  524 load-font
: font-rec ( id -- rec|0 )
   nfonts @ 0 ?do fonts i 2* cells + @ over = if
     drop fonts i 2* cells + cell+ @ unloop exit then loop drop 0 ;

\ the strike for text-font at text-size, and that strike's point size
: strike-size ( -- 9|12 ) text-size @ 10 < if 9 else 12 then ;
: cur-font ( -- rec base )
   text-font @ 128 * strike-size + font-rec ?dup if strike-size exit then
   text-font @ 128 * 12 + font-rec ?dup if 12 exit then
   12 font-rec 12 ;                              \ fall back to Chicago 12
: f-first sw@ ;  : f-last 2 + sw@ ;  : f-ascent 4 + sw@ ;
: f-height 10 + sw@ ;  : f-rb 12 + sw@ ;  : f-kern 14 + sw@ ;
: f-n ( rec -- n ) dup f-last swap f-first - 3 + ;
: f-loc ( idx rec -- n ) swap 2* + 16 + w@ ;
: f-ow  ( idx rec -- n ) dup f-n 2* 16 + + swap 2* + w@ ;
: f-strike ( rec -- a ) dup f-n 4 * 16 + + ;
\ glyph slot for c: the font's missing-glyph slot if it has no such char
: f-idx { c rec | i -- i }
   c rec f-first - -> i
   c rec f-first < c rec f-last > or if rec f-n 2 - -> i then
   i rec f-ow 65535 = if rec f-n 2 - -> i then  i ;

variable g-rec  variable g-base  variable g-w
: g-sc ( n -- n' ) text-size @ * g-base @ / ;
: g-bold? text-face @ 1 and 0<> ;  : g-outl? text-face @ 8 and 0<> ;
: g-adv ( idx -- w )   \ unscaled advance, style widening included
   g-rec @ f-ow 255 and  g-bold? 1 and +  g-outl? 1 and + ;

\ glyph mask: one byte per pixel, 1px margin all round for styling
64 constant mw   24 constant mh
create gm  mw mh * allot   create gm2 mw mh * allot
: gm@ ( x y -- v ) mw * + gm + c@ ;
: gm! ( v x y -- ) mw * + gm + c! ;
: g-bit ( col row -- v )
   g-rec @ f-rb * over 3 rshift + g-rec @ f-strike + c@
   128 rot 7 and rshift and 0<> 1 and ;
: g-load { idx | l0 w -- }   \ strike glyph -> gm, offset (1,1)
   gm mw mh * erase
   idx g-rec @ f-loc -> l0   idx 1+ g-rec @ f-loc l0 - mw 3 - min -> w
   w g-w !
   g-rec @ f-height mh 2 - min 0 ?do  w 0 ?do
     l0 i + j g-bit  i 1+ j 1+ gm!
   loop loop ;
: g-embolden ( -- )          \ QuickDraw bold: ink OR'd one pixel right
   mh 0 do 1 mw 1- do  i 1- j gm@ if 1 i j gm! then  -1 +loop loop ;
: g-outline ( -- )           \ outline: the ring around the ink, ink cleared
   gm gm2 mw mh * cmove
   mh 1- 1 do mw 1- 1 do
     i j mw * + gm2 + dup c@ 0= if
       dup 1- c@ over 1+ c@ or over mw - c@ or over mw + c@ or
       i j gm!
     else 0 i j gm! then drop
   loop loop ;

\ paint mask pixel (i,j) as a scaled block, glyph box origin at (x,y)
: g-px { i j x y -- }
   x i 1- g-sc +  y j 1- g-sc +  x i g-sc +  y j g-sc +
   black 8 cur-bmap @ (fill) ;
: gemit { c | idx x y adv -- }
   gfx-text @ 0= if exit then
   cur-font g-base ! g-rec !
   c g-rec @ f-idx -> idx
   idx g-adv g-sc -> adv
   pen-y @ g-rec @ f-ascent g-sc - -> y
   text-mode @ 3 and 0= if            \ srcCopy: the character cell goes white
     pen-x @ y  pen-x @ adv +  y g-rec @ f-height g-sc +
     white 8 cur-bmap @ (fill)
   then
   pen-x @ g-rec @ f-kern idx g-rec @ f-ow 8 rshift + g-sc + -> x
   idx g-load
   g-bold? if g-embolden then
   g-outl? if g-outline then
   g-rec @ f-height 2 + mh min 0 do  g-w @ 3 + 0 do
     i j gm@ if i j x y g-px then
   loop loop
   adv pen-x +! ;
: charwidth ( c -- w ) cur-font g-base ! g-rec !  g-rec @ f-idx g-adv g-sc ;
: stringwidth ( c$ -- w ) 0 swap count over + swap ?do i c@ charwidth + loop ;

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
