\ macforth-shim.fs -- MacForth (Mac 128K) compatibility layer for pforth (32-bit build)
\ Lets the original 1986 ChipWits+ source compile & run on Linux.
\ Rendering is real (see qd.fs): 1-bit bitmaps, patterns, CopyBits, bitmap text.
\ Menus/controls/TextEdit/sound remain arity-faithful stubs.

decimal

\ ==================== data-space: exact 68k struct layout ====================
\ MacForth lays structs with mixed , w, c, and no alignment padding.
: ,   here 4 allot ! ;
: w,  here 2 allot w! ;
: c,  here 1 allot c! ;

\ signed 16-bit fetch (rect fields hold negative coords)
: w@  w@ dup 32767 > if 65536 - then ;

\ A classic Mac app started with zeroed fresh memory, and the game relies on
\ it (bouncer.state, robot.program, ...).  pforth's dictionary is garbage, so
\ zero everything VARIABLE and ALLOT hand out.
: variable create 0 , ;
: allot ( n -- ) here over allot swap erase ;

\ MacForth CONSTANT has a patchable body (source does: ' name ! )
: constant create , does> @ ;
\ native tick ( "name" -- xt ), parsed at run time -- kept for vectoring below
: nt' ' ;
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
create pict-rect 8 allot
: >rect { x1 y1 x2 y2 -- x1 y1 rect }   \ PICT.IN.RECT (screen 190) only
   \ builds the destination rect but leaves x1 y1: the caller's closing
   \ 2DROP expects them (its stack only balances that way)
   x1 y1  y1 x1 y2 x2 pict-rect !rect  pict-rect ;
\ MAKE.RECT builds a rect VALUE from two corners (used as: make.rect drop , ,)
: make.rect { x1 y1 x2 y2 -- pbr ptl flag }
   x2 y2 xy>point  x1 y1 xy>point  0 ;
\ no locals here: pforth miscompiles a second { } block mid-definition
variable pt-x  variable pt-y
: ptinrect ( p a -- flag )
   swap point>xy pt-y ! pt-x !    ( a )
   pt-y @ over w@ < not           ( a y>=t )
   over 4 + w@ pt-y @ > and      ( a y-ok )
   swap                           ( y-ok a )
   pt-x @ over 2+ w@ < not        ( y-ok a x>=l )
   swap 6 + w@ pt-x @ > and      ( y-ok x-ok )
   and ;
: (offsetrect) { a dh dv -- }
   a w@ dv + a w!  a 2+ w@ dh + a 2+ w!
   a 4 + w@ dv + a 4 + w!  a 6 + w@ dh + a 6 + w! ;

\ patterns: 8-byte bit images
create white 0 , 0 ,
create black -1 , -1 ,
create gray  hex AA55AA55 , 55AA55AA , decimal
: pattern ( ? -- ) ;   \ revisit on use

\ ==================== screen / windows ====================
\ Classic Mac: 512x342 1-bit screen; every window's portBits points at it.
\ Window struct: +0 pad(2), +2 BitMap{ baseAddr(4) rowBytes(2) bounds(8) }
create the-screen 512 8 / 342 * allot
the-screen 512 8 / 342 * erase
variable cur-bmap
: (win) ( -- ) 0 w, the-screen , 64 w, 0 w, 0 w, 342 w, 512 w, 144 allot ;
create sys.window (win)
sys.window 2+ cur-bmap !
: new.window ( "name" -- ) create (win) ;
variable current-window  sys.window current-window !
: select.window ( w -- ) dup current-window ! 2+ cur-bmap ! ;
: add.window ( w -- ) drop ;
: get.window ( -- w ) current-window @ ;
: w.bounds ( t l b r w -- ) drop 2drop 2drop ;
: on.activate ( w -- ) drop ;
: window ( w -- ) select.window ;
: +wrefcon ( w -- a ) 140 + ;

\ ==================== transfer-mode constants ====================
0 constant srccopy   1 constant srcor    2 constant srcxor   3 constant srcbic
8 constant patcopy   9 constant pator   10 constant patxor  11 constant patbic
0 constant frame  1 constant paint  2 constant clear

\ ==================== the renderer ====================
include qd.fs
\ QD verb, shadows bitwise INVERT (game code never uses the bitwise one;
\ qd.fs already bound the bitwise version internally)
3 constant invert

\ ==================== toolbox trap defining words ====================
\ Screen 076/190 define CopyBits etc. via A-trap numbers; dispatch on trap#.
: do-trap ( ... trap# -- ... )
   case
     43244 of (copybits) endof                      \ A8EC CopyBits (real)
     43125 of cur-bmap ! endof                      \ A875 SetPortBits (real)
     43254 of (drawpicture) endof                   \ A8F6 DrawPicture (real)
     43131 of drop endof                            \ A87B ClipRect
     43225 of drop endof                            \ A8D9 DisposeRgn
     43427 of drop endof                            \ A9A3 ReleaseResource
     dup . ." <- unknown A-trap, stack may drift" cr
   endcase ;
: mt    ( trap# "name" -- ) create , does> @ do-trap ;
: w>mt  ( trap# "name" -- ) create , does> @ do-trap ;
: 2w>mt ( trap# "name" -- ) create ,
   does> @ 43176 = if (offsetrect) else drop 2drop drop then ;  \ A8A8 OffsetRect
: func>l ( trap# "name" -- ) create , does> drop 0 ;   \ returns nil handle

\ ==================== cursor / misc graphics ====================
: hide.cursor ( -- ) ;  : show.cursor ( -- ) ;  : init.cursor ( -- ) ;
: set.cursor ( c -- ) drop ;
0 constant ibeam
0 constant plain   8 constant outline
: scroll ( rect dx dy rgn -- ) 2drop 2drop ;
: global>local ( p -- p ) ;
variable xoff-v variable yoff-v variable xpiv-v variable ypiv-v
: xyoffset ( x y -- ) yoff-v ! xoff-v ! ;
: xypivot  ( x y -- ) ypiv-v ! xpiv-v ! ;
: get.xyoffset ( -- x y ) xoff-v @ yoff-v @ ;
: get.xypivot  ( -- x y ) xpiv-v @ ypiv-v @ ;

\ ==================== menus (stubs) ====================
: new.menu ( flags title$ menu# -- ) drop 2drop ;
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
\ MENU.SELECTION: everything after it in the definition is the menu's handler
\ ( item# -- ). During setup the word must END there; in dispatch mode the
\ handler runs with the picked item number.  Dispatch: set menu-item and
\ menu-mode, call the menu word, reset menu-mode.
variable menu-item   variable menu-mode
: (menu.sel) ( menu# -- item# | returns-from-caller )
   drop menu-mode @ if menu-item @ else r> drop then ;
: menu.selection: postpone (menu.sel) ; immediate

\ ==================== events (vectored; live.fs rebinds for real input) ====================
variable 'do.events   variable '@mouse   variable 'mouse.was..
variable 'still.down  variable '?keystroke
: (ev0) ( -- 0 ) 0 ;
nt' (ev0) 'do.events !    nt' (ev0) '@mouse !
nt' (ev0) 'mouse.was.. !  nt' (ev0) 'still.down !
nt' (ev0) '?keystroke !
: do.events ( -- event|0 ) 'do.events @ execute ;
: @mouse ( -- p ) '@mouse @ execute ;
: mouse.was.. ( -- p ) 'mouse.was.. @ execute ;
: still.down ( -- f ) 'still.down @ execute ;
: ?keystroke ( -- 0 | c -1 ) '?keystroke @ execute ;
: flush.events ( -- ) ;
6 constant mouse.down
: @mousexy ( -- x y ) @mouse point>xy ;

\ ==================== controls / TextEdit ====================
\ One implicit TE record (the game edits one name at a time).  Classic TERec
\ layout is honored where the game peeks: +60 teLength (w), +62 hText (handle).
create te-text 256 allot
create te-hText te-text ,
create te-rec 512 allot
te-rec 512 erase
te-hText te-rec 62 + !
: tenew ( r1 r2 -- h ) 2drop te-rec ;
: terecord ( w -- te ) drop te-rec ;
: teactivate ( -- ) ;  : tedeactivate ( -- ) ;
: teidle ( -- ) ;
: tekey ( c -- ) drop ;
: teset.select ( a b -- ) 2drop ;
: teset.text ( a n -- ) 255 min dup te-rec 60 + w! te-text swap cmove ;
: teupdate ( r -- ) drop ;
: text.click ( -- ) ;
: get.control ( ctl -- v ) drop -1 ;   \ headless: dialogs resolve instantly
: set.control ( ctl v -- ) 2drop ;
: hilite.control ( ? c -- ) 2drop ;
: kill.controls ( w -- ) drop ;
: this.control ( -- ? ) 0 ;
: toggle.control ( c -- ) drop ;
: track.control ( ? -- ? ) 0 ;
: ?in.control ( -- f ) 0 ;
variable ctl-counter
: binary.control ( w x y title$ value kind -- ctl )
   2drop drop 2drop drop  1 ctl-counter +!  ctl-counter @ ;

\ ==================== sound ====================
\ TONE ( duration volume freq*10 -- ): one square-wave note, duration in
\ ticks (1/60 s), volume 0-255, frequency in tenths of Hz (screen 057's
\ scale( table: 5233 = C5, 523.3 Hz).  Notes queue
\ behind each other like the Mac sound driver's; ?SOUND is true while the
\ queue is still playing, so the game's  begin ?sound not until  waits
\ for real time.  A note that would start more than half a second late is
\ dropped rather than letting the queue lag the game.
\ 'tone-hook ( dur vol f10 -- ) delivers each note (live.fs: the browser).
variable snd-busy   \ cmsec at which the queue drains
variable 'tone-hook
: ?sound ( -- f ) snd-busy @ cmsec - 0> ;
: tone { dur vol f10 | now -- }
   cmsec -> now
   snd-busy @ now - 0< if now snd-busy ! then
   snd-busy @ now - 500 > if exit then
   dur 1000 * 60 / snd-busy +!
   'tone-hook @ ?dup if >r dur vol f10 r> execute then ;
: aplay ( chord -- ) drop ;   \ old chord sounds: disabled in the source
: hush ( -- ) cmsec snd-busy ! ;

\ ==================== files (real, backed by recovered CW+ data files) ====================
\ MacForth file channels 0..15; data files live in data/
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
: get.eof ( f# -- n ) ffd @ ?dup if file-size drop drop else 0 then ;
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
\ PICT resources: tools/extract_resources.py renders each one to
\ data/pict-NNN.bin (t l b r rowBytes 0 as int16, then 1-bit rows).
\ A handle is a 2-cell record [ptr][size]; loaded once, kept for good.
create pict-handles 16 2* cells allot   \ ids 100..115
: get.picture { id | h fid len -- h }
   id 100 - 15 u> if 0 exit then
   pict-handles id 100 - 2* cells + -> h
   h @ if h exit then
   id s" pict-" data-path r/o open-file if drop 0 exit then -> fid
   fid file-size 2drop -> len
   len allocate drop h !  len h cell+ !
   h @ len fid read-file 2drop  fid close-file drop  h ;
: ?file.error ( -- ) ;
variable 'close-hook
: close.all ( -- ) 16 0 do i close loop
   'close-hook @ ?dup if execute then ;
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
: handle.size ( h -- n ) cell+ @ ;   \ only PICT handles are asked
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
