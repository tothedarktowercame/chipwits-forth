\ test-vm.fs -- headless smoke test of the ChipWits IBOL virtual machine
include loader.fs

decimal
cr ." === headless IBOL VM test ===" cr

1 adventure !                    \ Greedville
files.name                       \ assign CW/IBOL/Greedville channels + rec lens
doom.file open
doom.file ?open . ." <- doom.file open? " cr
init.game                        \ furnish room, place robot on a floor square

\ Hand-wire panel A, the classic wall-follower:
\ chip0:  FEEL.FOR WALL  true->down(chip10)  false->right(chip1)
\ chip1:  MOVE FORWARD   flow left -> chip0
\ chip10: MOVE TURN.RIGHT flow up -> chip0
robot.program prog.size@ erase
0 current.panel^ !
0 current.instruction^ !   feel.for@ 64 or   wall@ 192 or  !chip
1 current.instruction^ !   move@ 128 or      forward@      !chip
10 current.instruction^ !  move@             turn.right@   !chip
0 current.instruction^ !

game.on@ prog.status !

." start: square=" robot.square @ .
." orient=" robot.orientation @ .
." fuel=" fuel.reg @ .  ." depth=" depth . cr

: cycle-report ( n -- )
   ." cycle " 2 .r
   ."  sq=" robot.square @ 3 .r
   ."  or=" robot.orientation @ 2 .r
   ."  fuel=" fuel.reg @ 5 .r
   ."  dmg=" damage.reg @ 4 .r
   ."  chip#=" current.instruction^ @ 3 .r
   ."  depth=" depth 2 .r cr ;

20 0 do
   execute.robot.instruction
   i cycle-report
loop

cr ." === VM test done ===" cr
