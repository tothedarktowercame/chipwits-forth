\ live.fs -- playable bridge: real mouse/key/menu events from live/input.bin,
\ framebuffer published to live/frame.raw (serve.py talks to the browser).
include loader.fs

decimal

variable mx  variable my  variable btn  variable downpt
variable keych  variable keyf
variable ev-fd  variable ev-off  variable fr-fd  variable snd-fd
variable tick   variable ev-x  variable ev-y

: open-live
   s" live/input.bin" r/w open-file abort" run play.sh (no live/input.bin)" ev-fd !
   0 ev-off !
   s" live/frame.raw" w/o create-file abort" cannot create live/frame.raw" fr-fd !
   s" live/sound.bin" w/o create-file abort" cannot create live/sound.bin" snd-fd ! ;

\ notes for the browser: [duration][volume][freq*10][pad] as 16-bit LE
create sndbuf 8 allot
: live-tone ( dur vol f10 -- )
   sndbuf 4 + w!  sndbuf 2+ w!  sndbuf w!  0 sndbuf 6 + w!
   snd-fd @ ?dup if >r sndbuf 8 r@ write-file drop r> flush-file drop then ;

create evbuf 8 allot
: read-event ( -- t x y true | false )
   ev-fd @ 0= if false exit then
   ev-fd @ file-size drop drop  ev-off @ 8 + < if false exit then
   ev-off @ 0 ev-fd @ reposition-file drop
   evbuf 8 ev-fd @ read-file 2drop
   8 ev-off +!
   evbuf w@  evbuf 2+ w@  evbuf 4 + w@  true ;

: push-frame
   fr-fd @ 0= if exit then
   0 0 fr-fd @ reposition-file drop
   the-screen 21888 fr-fd @ write-file drop
   0 0 fr-fd @ reposition-file drop ;   \ second seek flushes stdio buffer

variable disp-depth
: menu-dispatch ( menu# item# -- )
   menu-item !  1 menu-mode !
   depth 1- disp-depth !
   case 5 of option.menu endof
        6 of ws.menu     endof
        7 of wareh.menu  endof
        8 of envir.menu  endof
   endcase  0 menu-mode !
   \ a menu word with a nested no-op handler (envir.menu.choices) leaves the
   \ item# behind; drop whatever the dispatch added
   begin depth disp-depth @ > while drop repeat ;

\ event records: [type][x][y][pad] as 16-bit LE:
\ 1 mouse-down  2 mouse-up  3 mouse-move  4 key (x=char)  5 menu (x=menu# y=item#)
\ DO.EVENTS is the one place events are delivered.  The game's polling
\ loops (chip drags, the name dialog) never call it -- on a Mac the mouse
\ updated by interrupt -- so STILL.DOWN and @MOUSE also read the queue, but
\ only apply motion, button-up and keys there.  A mouse-down or menu pick
\ they meet is parked for the next DO.EVENTS: swallowing it would lose the
\ click (the name dialog polls @mouse right before DO.EVENTS every pass).
variable pend-t  variable pend-x  variable pend-y   \ pend-t 0 = none
: pace ( -- )   8 msec  1 tick +!  tick @ 3 and 0= if push-frame then ;
: next-event ( -- t x y true | false )
   pend-t @ if pend-t @ pend-x @ pend-y @ true  0 pend-t ! exit then
   read-event ;
: apply-motion ( t x y -- )   \ types 2 3 4
   rot case
     2 of my ! mx ! 0 btn ! endof
     3 of my ! mx ! endof
     4 of drop keych ! keyf on endof
     nip nip
   endcase ;
: (pump) ( -- event|0 )   \ DO.EVENTS: deliver one event
   pace
   next-event 0= if 0 exit then
   ev-y ! ev-x !
   case
     1 of ev-x @ mx !  ev-y @ my !  -1 btn !
          ev-x @ ev-y @ xy>point downpt !  push-frame  mouse.down endof
     5 of ev-x @ ev-y @ menu-dispatch  push-frame  in.menubar endof
     dup ev-x @ ev-y @ apply-motion  0 swap
   endcase ;
: (poll) ( -- )   \ @MOUSE / STILL.DOWN: motion only
   pace
   pend-t @ if exit then
   begin read-event while
     rot dup >r -rot r>        \ t x y t  (no PICK: the shim's is 1-based)
     dup 1 = swap 5 = or if pend-y ! pend-x ! pend-t ! exit then
     apply-motion
   repeat ;

: live-events ( -- event|0 ) (pump) ;
: live-@mouse ( -- p ) (poll)  mx @ my @ xy>point ;
: live-was ( -- p ) downpt @ ;
: live-down? ( -- f ) (poll)  btn @ ;
: live-key ( -- 0 | c -1 ) keyf @ if keych @ -1 keyf off else 0 then ;

: live-close ( -- )
   ev-fd @ ?dup if close-file drop 0 ev-fd ! then
   fr-fd @ ?dup if close-file drop 0 fr-fd ! then
   snd-fd @ ?dup if close-file drop 0 snd-fd ! then ;
nt' live-close 'close-hook !

nt' live-events 'do.events !
nt' live-@mouse '@mouse !
nt' live-was    'mouse.was.. !
nt' live-down?  'still.down !
nt' live-key    '?keystroke !
nt' live-tone   'tone-hook !

: play
   back.buffer back.len@ erase   anim.buffer anim.len@ erase
   ttl.buffer ttl.len@ erase     ibol.buffer@ ibol.len erase
   source.buffer source.len erase
   open-live  +gfx
   -1 chipwits ;

play
