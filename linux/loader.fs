\ loader.fs -- replaces SCREEN 001 (Robotnik loader) of ChipWits+
\ Load order copied from screen 001, minus MacForth system setup.

include macforth-shim.fs

decimal
new.window gameboard.window
0 0 342 512 gameboard.window w.bounds
-gfx   \ progress messages below go to the console, not the bitmap

." [common] "     2 24 thru
." [anima] "     76 83 thru
." [ibol-gfx] "  86 95 thru
sys.window select.window   gameboard.window add.window
." [stat&name] " 160 177 thru
." [debug] "     96 105 thru
." [stack/reg] " 150 159 thru
." [workshop] "  106 137 thru
." [voc.chop] "  178 179 thru
." [190-192] "   190 192 thru
." [game] "      25 68 thru
gameboard.window select.window
." [69-71] "     69 71 thru
." [bads] "      138 141 thru
." [Game] "      72 load
." [chooser] "   181 186 thru
." [menu] "      142 149 thru
." [master] "    73 75 thru
sys.window select.window
gameboard.window on.activate
stats( 'stats-buf !  name$( 'names-buf !   \ big-endian on disk: see read.virtual
cr ." === ChipWits+ compiled === " cr
\ note: graphics text is left OFF (-gfx); tests turn it on around game calls
