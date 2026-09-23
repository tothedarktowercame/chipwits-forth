\ macforth-shim.fs -- MacForth (Mac 128K) compatibility layer for pforth (32-bit build)
\ Lets the original 1986 ChipWits+ source compile & run on Linux.
\ Graphics/sound/file words are stubs with faithful arities for now;
\ rects, points and OffsetRect are implemented for real.

decimal

\ ==================== data-space: exact 68k struct layout ====================
\ MacForth lays structs with mixed , w, c, and no alignment padding.
: ,   here 4 allot ! ;
: w,  here 2 allot w! ;
: c,  here 1 allot c! ;

\ signed 16-bit fetch (rect fields hold negative coords)
: w@  w@ dup 32767 > if 65536 - then ;

\ MacForth CONSTANT has a patchable body (source does: ' name ! )
: constant create , does> @ ;
\ MacForth ' returns the patchable body; inside a definition it binds at compile time
: '  ' >body state @ if postpone literal then ; immediate

\ fig-style 1-based PICK and ROLL
: pick 1- pick ;
: roll 1- roll ;

\ ==================== fundamentals ====================
: not 0= ;
: on  ( a -- ) -1 swap ! ;
: off ( a -- ) 0 swap ! ;
: @@  ( a -- x ) @ @ ;
: 1+ 1 + ;   : 1- 1 - ;   : 2+ 2 + ;   : 2- 2 - ;
: 3+ 3 + ;   : 3- 3 - ;   : 4+ 4 + ;   : 4- 4 - ;
: 5+ 5 + ;   : 6+ 6 + ;   : 6- 6 - ;   : 7+ 7 + ;
: 8+ 8 + ;   : 8- 8 - ;   : 10+ 10 + ; : 10- 10 - ;
: 16+ 16 + ; : 16- 16 - ;
: 2* 2 * ;   : 2/ 2 / ;   : 4* 4 * ;   : 4/ 4 / ;
: 8* 8 * ;   : 8/ 8 / ;   : 16* 16 * ; : 16/ 16 / ;
\ MacForth I-fused loop words: IC@ = I C@, I+ = I +, etc.
: i+  postpone i postpone + ; immediate
: i@  postpone i postpone @ ; immediate
: ic@ postpone i postpone c@ ; immediate
: ic! postpone i postpone c! ; immediate
: i+@ postpone i postpone + postpone @ ; immediate
: blanks ( a n -- ) blank ;

variable rnd-seed  123457 rnd-seed !
: random ( -- u16 ) rnd-seed @ 1103515245 * 12345 + dup rnd-seed !
   16 rshift 65535 and ;

\ SIN ( deg -- sin*10000 ); no float literals (pforth-safe): pi ~ 355/113
: sin ( deg -- n ) s>f 355 s>f f* 113 s>f f/ 180 s>f f/ fsin
   10000 s>f f* f>s ;

\ CASE range extension: pforth's RANGEOF has identical semantics
: range.of postpone rangeof ; immediate

\ counted-string support: ," compiles into data space; " gives a literal
: ,"  ( "text" -- ) [char] " parse dup c, dup 0= if 2drop exit then
   over + swap do i c@ c, loop ;

create str-pool 16384 allot
variable str-pool>  0 str-pool> !
: pool$ { a n -- c$ }
   str-pool> @ n + 2 + 16300 > abort" string pool overflow"
   str-pool str-pool> @ +
   n over c!
   a over 1+ n cmove
   n 1+ str-pool> +! ;
: " ( "text" -- c$ ) [char] " parse pool$
   state @ if postpone literal then ; immediate

: error" postpone abort" ; immediate

\ MacForth screen-loading
create scr-fname 128 allot
variable fn-len
: fn+ ( a n -- ) dup >r scr-fname fn-len @ + swap cmove r> fn-len +! ;
: load ( n -- )
   0 fn-len !
   s" screens/" fn+
   0 <# # # # #> fn+
   s" .fs" fn+
   scr-fname fn-len @ included ;
: thru ( a b -- ) 1+ swap do i load loop ;
: list ( n -- ) drop ;
: empty-buffers ;
create first-buf 1024 allot
first-buf constant first

\ ==================== QuickDraw data structures (real) ====================
\ Rect = 4 x int16: top left bottom right.  Point p: x=hi16, y=lo16
\ (matches little-endian 32-bit fetch of {top,left} halfwords).
: !rect ( t l b r a -- ) >r r@ 6 + w! r@ 4 + w! r@ 2 + w! r> w! ;
: @rect ( a -- t l b r ) dup w@ swap dup 2+ w@ swap dup 4 + w@ swap 6 + w@ ;
: rect ( t l b r "name" -- ) create here 8 allot !rect ;
: point>xy ( p -- x y )
   dup 16 rshift dup 32767 > if 65536 - then
   swap 65535 and dup 32767 > if 65536 - then ;
: xy>point ( x y -- p ) 65535 and swap 65535 and 16 lshift or ;
: >rect ( x y -- y x ) swap ;  \ arity guess; revisit on use
\ MAKE.RECT builds a rect VALUE from two corners (used as: make.rect drop , ,)
: make.rect { x1 y1 x2 y2 -- pbr ptl flag }
   x2 y2 xy>point  x1 y1 xy>point  0 ;
: ptinrect { p a -- flag }
   p point>xy { x y }
   y a w@ < not  y a 4 + w@ < and
   x a 2+ w@ < not and  x a 6 + w@ < and ;
: (offsetrect) { a dh dv -- }
   a w@ dv + a w!  a 2+ w@ dh + a 2+ w!
   a 4 + w@ dv + a 4 + w!  a 6 + w@ dh + a 6 + w! ;

\ ==================== toolbox trap defining words ====================
\ Screen 076 defines CopyBits etc. via A-trap numbers; dispatch on trap#.
: do-trap ( ... trap# -- ... )
   case
     43244 of drop drop drop drop drop drop endof   \ A8EC CopyBits (6 args)
     43125 of drop endof                            \ A875 SetPortBits
     43254 of 2drop endof                           \ A8F6 DrawPicture
     43131 of drop endof                            \ A87B ClipRect
     43176 of (offsetrect) endof                    \ A8A8 OffsetRect (real)
     43225 of drop endof                            \ A8D9 DisposeRgn
     dup . ." <- unknown A-trap, stack may drift" cr
   endcase ;
: mt    ( trap# "name" -- ) create , does> @ do-trap ;
: w>mt  ( trap# "name" -- ) create , does> @ do-trap ;
: 2w>mt ( trap# "name" -- ) create , does> @ do-trap ;
: func>l ( trap# "name" -- ) create , does> drop 0 ;   \ returns nil handle

\ ==================== screen / windows ====================
\ Classic Mac: 512x342 1-bit screen; every window's portBits points at it.
\ Window struct: +0 pad(2), +2 BitMap{ baseAddr(4) rowBytes(2) bounds(8) }
create the-screen 512 8 / 342 * allot
: (win) ( -- ) 0 w, the-screen , 64 w, 0 w, 0 w, 342 w, 512 w, 144 allot ;
create sys.window (win)
: new.window ( "name" -- ) create (win) ;
variable current-window  sys.window current-window !
: select.window ( w -- ) current-window ! ;
: add.window ( w -- ) drop ;
: get.window ( -- w ) current-window @ ;
: w.bounds ( t l b r w -- ) drop 2drop 2drop ;
: on.activate ( w -- ) drop ;
: window ( w -- ) select.window ;
: close ( w|file# -- ) drop ;
: +wrefcon ( w -- a ) 140 + ;

\ ==================== QuickDraw drawing (stubs, faithful arity) ====================
0 constant srccopy   1 constant srcor    2 constant srcxor   3 constant srcbic
8 constant patcopy   9 constant pator   10 constant patxor  11 constant patbic
0 constant frame  1 constant paint  2 constant clear  3 constant invert-verb
: rectangle ( t l b r mode -- ) drop 2drop 2drop ;
: oval      ( t l b r mode -- ) drop 2drop 2drop ;
: rrectangle ( t l b r ow oh mode -- ) drop 2drop 2drop 2drop ;
: vector    ( x1 y1 x2 y2 -- ) 2drop 2drop ;
: move.to   ( x y -- ) 2drop ;
: draw.to   ( x y -- ) 2drop ;
: rmove     ( dx dy -- ) 2drop ;
: rdraw     ( dx dy -- ) 2drop ;
: @pen      ( -- x y ) 0 0 ;
: pensize   ( w h -- ) 2drop ;
: penmode   ( n -- ) drop ;
: penpat    ( pat -- ) drop ;
: backpat   ( pat -- ) drop ;
: ginit     ( -- ) ;
: hide.cursor ( -- ) ;  : show.cursor ( -- ) ;  : init.cursor ( -- ) ;
: set.cursor ( c -- ) drop ;
0 constant ibeam
0 constant plain   8 constant outline
: textfont  ( n -- ) drop ;
: textsize  ( n -- ) drop ;
: textstyle ( n -- ) drop ;
: textmode  ( n -- ) drop ;
: stringwidth ( c$ -- w ) count nip 8 * ;
: scroll ( rect dx dy rgn -- ) 2drop 2drop ;
: global>local ( p -- p ) ;
variable xoff-v variable yoff-v variable xpiv-v variable ypiv-v
: xyoffset ( x y -- ) yoff-v ! xoff-v ! ;
: xypivot  ( x y -- ) ypiv-v ! xpiv-v ! ;
: get.xyoffset ( -- x y ) xoff-v @ yoff-v @ ;
: get.xypivot  ( -- x y ) xpiv-v @ ypiv-v @ ;
create white 0 , 0 ,
create black -1 , -1 ,
create gray  hex AA55AA55 , 55AA55AA , decimal
: pattern ( ? -- ) ;   \ revisit on use

\ ==================== menus (stubs) ====================
: new.menu ( ? -- ? ) ;          \ revisit on use
: delete.menu ( n -- ) drop ;
: draw.menu.bar ( -- ) ;
: hilite.menu ( n -- ) drop ;
: item.check  ( ? ? m -- ) drop 2drop ;
: item.enable ( ? ? m -- ) drop 2drop ;
: item.style  ( ? ? m -- ) drop 2drop ;
: menu.enable ( ? m -- ) 2drop ;
: set.item$   ( ? ? m -- ) drop 2drop ;
: append.items ( ? m -- ) 2drop ;
: in.menubar  ( -- code ) 1 ;
: menu.selection: ( ? -- ) ;     \ revisit on use

\ ==================== events (headless: nothing happens) ====================
: do.events ( -- event|0 ) 0 ;
: flush.events ( -- ) ;
6 constant mouse.down
: mouse.was.. ( -- code ) 0 ;
: @mouse ( -- p ) 0 ;
: @mousexy ( -- x y ) 0 0 ;
: still.down ( -- f ) 0 ;
: ?keystroke ( -- 0 | c -1 ) 0 ;

\ ==================== controls / TextEdit (stubs) ====================
create te-rec 512 allot
: tenew ( r1 r2 -- h ) 2drop te-rec ;
: terecord ( -- ? ) te-rec ;
: teactivate ( h -- ) drop ;  : tedeactivate ( h -- ) drop ;
: teidle ( h -- ) drop ;
: tekey ( c h -- ) 2drop ;
: teset.select ( a b h -- ) drop 2drop ;
: teset.text ( a n h -- ) drop 2drop ;
: teupdate ( r h -- ) 2drop ;
: text.click ( p ? h -- ) drop 2drop ;
: get.control ( ? -- ? ) ;      \ revisit on use
: set.control ( ? -- ? ) ;      \ revisit on use
: hilite.control ( ? c -- ) 2drop ;
: kill.controls ( w -- ) drop ;
: this.control ( -- ? ) 0 ;
: toggle.control ( c -- ) drop ;
: track.control ( ? -- ? ) 0 ;
: ?in.control ( p -- c t | f ) drop 0 ;
variable ctl-counter
: binary.control ( w x y title$ value kind -- ctl )
   2drop drop 2drop drop  1 ctl-counter +!  ctl-counter @ ;

\ ==================== sound (stubs) ====================
: tone ( amp dur pitch -- ) drop 2drop ;
: aplay ( chord -- ) drop ;
: ?sound ( -- f ) 0 ;
: hush ( -- ) ;

\ ==================== files (real, backed by recovered CW+ data files) ====================
\ MacForth file channels 0..15; data files live in chipwits-native/data/
create file-names 16 64 * allot   file-names 16 64 * erase
create file-fds   16 cells allot  file-fds 16 cells erase
create file-lens  16 cells allot  file-lens 16 cells erase
: fname ( f# -- a ) 15 and 64 * file-names + ;
: ffd   ( f# -- a ) 15 and cells file-fds + ;
: flen  ( f# -- a ) 15 and cells file-lens + ;
: file? ( x -- x flag ) dup 0 < not over 16 < and ;   \ CLOSE also gets windows
: assign { c$ f# -- }
   c$ c@ 1+ c$ f# fname rot cmove ;
: remove ( f# -- ) file? if
     dup ffd dup @ ?dup if close-file drop then 0 swap !
     fname 64 erase
   else drop then ;
: (fpath) ( f# -- a n ) 0 fn-len !
   s" data/" fn+
   fname count fn+  scr-fname fn-len @ ;
: open ( f# -- ) file? not if drop exit then
   dup (fpath) r/w open-file
   if drop 0 else then swap ffd ! ;
: ?open ( f# -- flag ) ffd @ 0<> ;
: close ( f#|window -- ) file? if
     ffd dup @ ?dup if close-file drop then 0 swap !
   else drop then ;
: get.eof ( f# -- n ) ffd @ ?dup if file-size drop drop ( lo hi->lo ) else 0 then ;
variable cur-fd
: read.virtual { a len off f# -- }
   f# ffd @ ?dup if cur-fd !
     off 0 cur-fd @ reposition-file drop
     a len cur-fd @ read-file 2drop
   then ;
: write.virtual { a len off f# -- }
   f# ffd @ ?dup if cur-fd !
     off 0 cur-fd @ reposition-file drop
     a len cur-fd @ write-file drop
   then ;
: read.fixed { a rec f# -- }
   f# ffd @ ?dup if cur-fd !
     rec f# flen @ * 0 cur-fd @ reposition-file drop
     a f# flen @ cur-fd @ read-file 2drop
   then ;
: write.fixed { a rec f# -- }
   f# ffd @ ?dup if cur-fd !
     rec f# flen @ * 0 cur-fd @ reposition-file drop
     a f# flen @ cur-fd @ write-file drop
   then ;
: set.rec.len ( len f# -- ) flen ! ;
: get.picture ( n f# -- h ) 2drop 0 ;   \ PICT decoding: later (graphics pass)
: ?file.error ( -- ) ;
: close.all ( -- ) ;
: copy ( ? ? -- ) 2drop ;

\ ==================== heap / memory management ====================
: ?heap.size ( -- free ) 999999 ;
: rsrvmem ( size -- size ) ;
variable heap-handle-slot
: from.heap ( size -- handle ) allocate drop heap-handle-slot ! heap-handle-slot ;
: to.heap ( handle -- ) drop ;
: lock.handle ( h -- ) drop ;
: non.purgable ( h -- ) drop ;
: mask.handle ( a -- a ) ;
: handle.size ( h -- n ) drop 0 ;
: resize.object ( n -- ) drop ;
: resize.vocab ( n -- ) drop ;
: minimum.vocab ( n -- ) drop ;
: minimum.object ( n -- ) drop ;
: set.fence ( -- ) ;
: release.resource ( h -- ) drop ;

\ ==================== printer ====================
variable printer
: print ( ? -- ) ;        \ revisit on use
: print.bits ( ? -- ) ;
: print.screen ( -- ) ;
: rst.printer ( -- ) ;
: screen;print ( -- ) ;

\ ==================== misc ====================
: ?terminal ( -- f ) 0 ;
: +tvisrect ( ? -- ? ) ;   \ revisit on use
: it ;                     \ FORGET IT anchor
