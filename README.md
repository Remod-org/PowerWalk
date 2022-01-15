# PowerWalk
Allow players to climb power poles and walk the lines

## Commands

  -- `/pwalk show` -- Show the location of all powerlines

  -- `/pwalk XX tp` -- Teleport to the start of powerline XX

  -- `/pwalk XX` -- Spawn ladders and walking platform for powerline XX, starting with the closest point in that line and extending up in one direction

## Permissions

  -- `powerwalk.use` -- Allows use of the /pwalk command

## Configuration

```json
{
  "Options": {
    "ShowAllTextTime": 30.0,
    "ShowOneTextTime": 60.0,
    "ShowOneAllPoints": true,
    "debug": true
  },
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 1
  }
}
```
