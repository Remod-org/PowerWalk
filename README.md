# PowerWalk
Allow players to climb power poles and walk the lines

Actually, we spawn ladders up the nearest pole and platforms (floor/triangle) to the next pole on either side of that pole.  As the player moves, so does the construction/destruction of the ladders and platforms.

By default, the platforms should not be visible to other players.

The simplest way to use this:
  1. Assign `powerwalk.use` permission to player or group.
  2. Walk nearby any power line.
  3. Enter `/pwalk` in chat.

## Commands

  -- `/pwalk show` -- Show the location of all powerlines

  -- `/pwalk XX tp` -- Teleport to the start of powerline XX

  -- `/pwalk XX` -- Spawn ladders and walking platform for powerline XX, starting with the closest point in that line and extending up in one direction

  -- `/pwalk` -- Spawn ladders and walking platform for the closest powerline, starting with the closest point in that line

## Permissions

  -- `powerwalk.use` -- Allows use of the /pwalk command

  -- `powerwalk.tp` -- Allows use of the /pwalk tp command

## Configuration

```json
{
  "Options": {
    "ShowAllTextTime": 30.0,
    "ShowOneTextTime": 60.0,
    "ShowOneAllPoints": true,
    "ShowPlatformsToAll": false,
    "debug": false
  },
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 4
  }
}
```
