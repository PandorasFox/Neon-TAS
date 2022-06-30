# Neon TAS

## Caveats and Notes

Inputs are replayed by hooking on Unity's fixed-rate `fixedUpdate` routine that ticks every .0167 of game-time and updating what input polls will receive until the next fixedUpdate tick. However, a fair amount of movement/velocity/input processing happens in `.Update` methods that tick with every frame, rather than at a fixed rate.

What this means is that inputs should be updated at a constant-interval 60hz rate, but input polling _might not_ fall evenly on those if your framerate varies too much.

Inputs are recorded by reading input states on every fixedUpdate tick, so recording may miss inputs if you input them for 

It is recommended to use specific frame limits when using these tools. I haven't done much testing on how much this matters, but if the VOD of the TAS will be at 60fps anyways, may as well do all the TASing at 60fps :)

