#!/usr/bin/env python3
"""Check the map assets' own format, without opening the Editor.

A vein is written in one place: the `Ore:` list. The `Terrain:` byte under it stays plain, because the
conversion lets a shape patch claim its cells as ore whatever the array says (MapDefinition.StampTerrain).
What is wrong in a map file is silent in a run, and every rule below is one of those:

  * a `Terrain:` byte that says Ore. With no entry naming the cell, the world still gets an ore tile -
    one only a drill may stand on, and one that yields nothing when it does. With an entry beside it, the
    byte is the half-write that leaves exactly that tile behind the moment the entry is taken away by
    hand;
  * two `- Cell:` entries for one cell. The first entry is the one that counts, here and in the
    conversion alike, so the second is an edit that does nothing;
  * an ore entry naming no mineral: it creates no patch at all, claims no cell as ore, and leaves the
    ground under it plain - the entry is simply ignored, like the duplicate above;
  * an entry outside the map, or a terrain array that does not describe it;
  * a wave group naming no weakness shape. Every group is weak to one exact item - the demand the player
    has to build a line for - and a group without one can only ever be shot at a half damage, which is
    a wave nobody can beat rather than a hard one;
  * a map with no wave rows and no generator: nothing will ever arrive, so it can be neither won nor lost;
  * a map that hands its waves to the generator while no vein names a mineral: the generator roots its
    legal demands at the map's own minerals, so it would have nothing to ask for and every generated wave
    would arrive empty;
  * a tutorial row with no text, a guide tile off the map, a step this build has no rule for, or a line
    about a building no earlier line hands over - the last one is the expensive mistake: the line can never
    tick, so the lesson never completes and every wave it gates waits forever, on a screen with nothing
    coming;
  * a tutorial line or hint longer than the card that prints it: the HUD's type shrink has a floor, and
    past it the sentence spills out of the task card's edge.

The rules themselves are MapValidation's (`OreInTerrain`, `PatchWithoutMineral`, `GroupWithoutDemand`,
`NoWaves`, `GeneratorWithoutMineral`) and MapAsset.Validate's (`DuplicateOre`, and the four tutorial rules):
they run in the painting window and in the test suite, and this is the same answers for a file, which is
where a hand edit - a script's, or an agent's - lands before either of those ever sees it.

One rule is deliberately not here: whether a named demand is one this map's minerals and unlocked
operators can actually build. That is `DemandNotBuildable`, and answering it means walking the shape
closure - the algebra itself - which lives in Core and is the validator's job; a second implementation of
it in this file would be a second answer to the same question.

Usage:  python Tools/check-maps.py [map.asset ...]     (default: every map under Assets/Data/Maps)
"""

import re
import sys
from pathlib import Path

ORIGIN = Path(__file__).resolve().parent.parent
DEFAULT_DIR = ORIGIN / 'Assets' / 'Data' / 'Maps'

CELL = re.compile(r'^\s*- Cell: \{x: (-?\d+), y: (-?\d+)\}')
MINERAL = re.compile(r'^\s*Mineral: (\d+)')
TERRAIN = re.compile(r'^  Terrain: ([0-9a-fA-F]*)\s*$')
SIZE = re.compile(r'^  (Width|Height): (-?\d+)\s*$')
WAVES = re.compile(r'^  Waves:\s*$')
GROUP = re.compile(r'^\s+- Kind: \d+\s*$')
INTERMISSION = re.compile(r'^\s+- Intermission:')
WEAKNESS = re.compile(r'^\s+Weakness:\s*$')
QUADRANT = re.compile(r'^\s+(NW|NE|SE|SW): (\d+)\s*$')
GENERATED = re.compile(r'^  GeneratedWaves: (-?\d+)\s*$')
UNLOCKED = re.compile(r'^  Unlocked: (\d+)\s*$')
TUTORIAL_STARTING = re.compile(r'^  TutorialStarting:\s*$')
TUTORIAL_TASKS = re.compile(r'^  TutorialTasks:\s*$')
TASK_STEP = re.compile(r'^  - Step: (-?\d+)\s*$')
TASK_TEXT = re.compile(r'^    Text: (.*)$')
TASK_HINT = re.compile(r'^    Hint: (.*)$')
TASK_TARGET = re.compile(r'^    Target: \{x: (-?\d+), y: (-?\d+)\}\s*$')
TASK_OPENS = re.compile(r'^    Opens: (.*)$')
TASK_OPENS_ROW = re.compile(r'^    - (\d+)\s*$')
TASK_WAVE = re.compile(r'^    StartsWave: (\d+)\s*$')
STARTING_ROW = re.compile(r'^  - (\d+)\s*$')
TASK_NULL = re.compile(r'^  - \{fileID: 0\}\s*$')

GROUND, ORE, ROCK, WATER, VOID = 0, 1, 2, 3, 4
NAMES = {GROUND: 'Ground', ORE: 'Ore', ROCK: 'Rock', WATER: 'Water', VOID: 'Void'}

CIRCLE, SQUARE = 1, 2

# The lesson's own vocabulary, spelled as the runtime spells it. A step names the building whose presence
# its rule reads (TutorialScript.TryRequiredBuilding), and a building names the operator a map has to have
# handed over before it may be built at all (MachineDef.Requires, derived from the behaviour).
BELT, DRILL, CUTTER, TURRET = 0, 1, 2, 9
STEPS = {
    0: ('PlaceDrill', DRILL),
    1: ('BankShape', DRILL),
    2: ('ArmTurret', TURRET),
    3: ('MakeHalf', CUTTER),
    4: ('BeltFork', CUTTER),
    5: ('ArmTurretWithHalf', TURRET),
}
BUILDING_NAMES = {DRILL: 'drill', CUTTER: 'cutter', TURRET: 'turret'}
REQUIRES = {DRILL: 0, TURRET: 0, CUTTER: 1}            # Operators.Cutter is the one flag a lesson can need
OPERATORS_ALL = 7                                      # Operators.All, the field's own default

# MapAsset.TutorialTextBudget, spelled a second time: the task card's column of micro type holds about this
# many characters at its shrink floor, and a line past it spills past the card onto the map.
TEXT_BUDGET = 88


def read(path):
    """What this check needs: the size, the array, the ore list, the waves, the generator's bound and the
    lesson.

    The waves come back as one list per authored row, each holding one quadrant list per group, because
    "- Intermission:" is the only line that starts a row: it is the marker the panel's numbering and this
    check's messages both have to agree on.

    The lesson comes back as one dict per row, with the fields a rule reads and the row's own position for
    the messages - a line is named by its number, because that is what the HUD counts against.
    """
    width = height = None
    terrain = None
    generated = None
    unlocked = None
    ore = []
    pending = None

    in_waves = False
    waves = []            # one entry per authored wave: one quadrant list per group
    wave = None
    group = None

    in_starting = False
    starting = []
    tutorial = []         # one dict per authored row; empty for a map that teaches nothing
    row = None

    for line in path.read_text(encoding='utf-8').splitlines():
        size = SIZE.match(line)
        if size:
            in_starting = False
            if size.group(1) == 'Width':
                width = int(size.group(2))
            else:
                height = int(size.group(2))
            continue

        array = TERRAIN.match(line)
        if array:
            in_starting = False
            terrain = array.group(1)
            continue

        bound = GENERATED.match(line)
        if bound:
            in_starting = False
            generated = int(bound.group(1))
            continue

        allowed = UNLOCKED.match(line)
        if allowed:
            in_starting = False
            unlocked = int(allowed.group(1))
            continue

        if TUTORIAL_STARTING.match(line):
            in_starting = True
            continue

        if TUTORIAL_TASKS.match(line):
            in_starting = False
            row = None
            continue

        # "- Step:" is a tutorial row's marker and "- <digit>" is a starting building, so the two are read
        # apart by the word after the dash: a row is judged on its fields, a starting kind is a number.
        step = TASK_STEP.match(line)
        if step:
            in_starting = False
            row = {'step': int(step.group(1)), 'opens': []}
            tutorial.append(row)
            continue

        if TASK_NULL.match(line):
            in_starting = False
            row = {}
            tutorial.append(row)
            continue

        if row is not None:
            text = TASK_TEXT.match(line)
            if text:
                row['text'] = text.group(1)
                continue

            hint = TASK_HINT.match(line)
            if hint:
                row['hint'] = hint.group(1)
                continue

            target = TASK_TARGET.match(line)
            if target:
                row['target'] = (int(target.group(1)), int(target.group(2)))
                continue

            opens = TASK_OPENS.match(line)
            if opens:
                row['opens'] = []          # "Opens: []" and "Opens:" followed by rows start the same way
                continue

            hand = TASK_OPENS_ROW.match(line)
            if hand:
                row['opens'].append(int(hand.group(1)))
                continue

            starts = TASK_WAVE.match(line)
            if starts:
                row['starts'] = starts.group(1) != '0'
                continue

        if in_starting:
            opened = STARTING_ROW.match(line)
            if opened:
                starting.append(int(opened.group(1)))
                continue

        if WAVES.match(line):
            in_waves = True
            continue

        if in_waves:
            if INTERMISSION.match(line):
                wave = []
                waves.append(wave)
                group = None
                continue

            if GROUP.match(line):
                group = []
                wave.append(group)
                continue

            if WEAKNESS.match(line):
                continue

            quadrant = QUADRANT.match(line)
            if quadrant and group is not None:
                group.append(int(quadrant.group(2)))
                continue

        cell = CELL.match(line)
        if cell:
            pending = (int(cell.group(1)), int(cell.group(2)))
            continue

        mineral = MINERAL.match(line)
        if mineral and pending is not None:
            ore.append((pending, int(mineral.group(1))))
            pending = None

    return width, height, terrain, ore, waves, generated, unlocked, starting, tutorial


def check_tutorial(rows, starting, unlocked, width, height):
    """The lesson's authoring rules, against a file: what a line must say, and what it must be able to reach.

    The reach rule is the one worth having here: a line whose building no earlier line hands over, or whose
    building the map's own operator set refuses, can never tick - so the lesson never completes and every
    wave it gates waits forever, on a screen with nothing coming. The row's own Opens do not count: they pay
    out on the tick the line is satisfied, which is the lesson that starts finished.
    """
    problems = []
    opened = list(starting)

    for number, row in enumerate(rows, start=1):
        text = row.get('text', '')
        if not text.strip():
            problems.append(f'tutorial line {number} has no text, so the HUD prints a blank line the player '
                            f'is told to act on')
        elif len(text) > TEXT_BUDGET:
            problems.append(f'tutorial line {number} runs {len(text)} characters - the task card holds about '
                            f'{TEXT_BUDGET} at its smallest type, so the line would spill past the card onto '
                            f'the map')

        hint = row.get('hint', '')
        if len(hint) > TEXT_BUDGET:
            problems.append(f'tutorial line {number}\'s hint runs {len(hint)} characters - the task card '
                            f'holds about {TEXT_BUDGET} at its smallest type, so the hint would spill past '
                            f'the card onto the map')

        target = row.get('target')
        if target is not None:
            x, y = target
            # A negative target is the authored "no place to point" - the Inspector shows (-1,-1) - so only
            # the top of the grid is a fault.
            if x >= width or y >= height:
                problems.append(f'tutorial line {number} points at ({x},{y}), which is outside the '
                                f'{width}x{height} map')

        step = row.get('step')
        if step is None:
            pass                                    # the null row above, already reported as having no text
        elif step not in STEPS:
            problems.append(f'tutorial line {number} asks for step {step}, which is not a lesson this build '
                            f'knows - the line can never tick, so every wave it gates waits forever')
        else:
            name, building = STEPS[step]
            what = BUILDING_NAMES[building]
            if REQUIRES[building] & ~unlocked:
                problems.append(f'tutorial line {number} is a {name} line - it needs a {what}, and this '
                                f'map\'s operator set (Unlocked: {unlocked}) never unlocks one, so the line '
                                f'can never tick and every wave it gates waits forever')
            elif building not in opened:
                problems.append(f'tutorial line {number} is a {name} line - it needs a {what}, and no '
                                f'earlier line hands one over (TutorialStarting: {starting}), so the line '
                                f'can never tick and every wave it gates waits forever')

        opened.extend(row.get('opens', []))

    return problems


def check(path):
    width, height, terrain, ore, waves, generated, unlocked, starting, tutorial = read(path)
    problems = []

    if width is None or height is None:
        return [f'{path.name}: no Width/Height to check against']

    if terrain is not None and len(terrain) != width * height * 2:
        problems.append(f'the terrain array is {len(terrain)} hex digits, and {width}x{height} wants '
                        f'{width * height * 2} - the size and the ground disagree')

    seen = set()
    minerals = set()
    for ((x, y), mineral) in ore:
        if not (0 <= x < width and 0 <= y < height):
            problems.append(f'the ore list names ({x},{y}), which is off the {width}x{height} map')
            continue

        if (x, y) in seen:
            problems.append(f'the ore list names ({x},{y}) twice: only the first entry counts, so the '
                            f'mineral named after it is ignored')
            continue

        if mineral not in (CIRCLE, SQUARE):
            problems.append(f'the ore at ({x},{y}) names mineral {mineral}, which is a hole or a '
                            f'mineral this build does not have, so the entry is ignored and no vein is '
                            f'created there')
            continue

        seen.add((x, y))
        minerals.add(mineral)

    if terrain is not None and len(terrain) == width * height * 2:
        for y in range(height):
            for x in range(width):
                i = y * width + x
                value = int(terrain[2 * i:2 * i + 2], 16)
                if value != ORE:
                    continue

                problems.append(f'the terrain array says Ore at ({x},{y})' + (
                    ', and the vein is listed there too: a vein is the ore list\'s entry, and the ground '
                    'under one is plain'
                    if (x, y) in seen else
                    ', and no vein is listed there: a drill would stand on it and mine nothing'))

    for number, groups in enumerate(waves, start=1):
        for index, quadrants in enumerate(groups, start=1):
            if not quadrants or not any(quadrants):
                problems.append(f'wave {number}\'s group {index} names no weakness shape: every group is '
                                f'weak to one exact item, and a group without one can only ever be shot at '
                                f'a half damage')

    # A missing GeneratedWaves line is the field's own default, 0 - and an authored row counts as a wave
    # however many groups it holds, exactly as MapValidation's NoWaves rule reads it.
    if not waves and not generated:
        problems.append('the map has no waves and no generator (GeneratedWaves: 0), so nothing will ever '
                        'arrive and the map can be neither won nor lost')

    if generated and not minerals:
        problems.append(f'the map hands its waves to the generator (GeneratedWaves: {generated}) but no '
                        f'ore entry names a mineral, so the generator has nothing to demand from and '
                        f'every generated wave arrives empty')

    if tutorial:
        problems.extend(check_tutorial(tutorial, starting, OPERATORS_ALL if unlocked is None else unlocked,
                                       width, height))

    for problem in problems:
        print(f'  {problem}')

    return problems


def main():
    args = sys.argv[1:]
    paths = [Path(a) for a in args] if args else sorted(DEFAULT_DIR.glob('*.asset'))
    if not paths:
        print(f'no map assets under {DEFAULT_DIR}')
        return 1

    total = 0
    for path in paths:
        if not path.is_file():
            print(f'{path}: not a file')
            total += 1
            continue

        print(f'{path.name}:')
        problems = check(path)
        total += len(problems)
        if not problems:
            print('  ok - the ore list is the only place a vein is written, every group names a demand, and '
                  'the lesson can be taught')

    print()
    print(f'{len(paths)} map(s), {total} problem(s)')
    return 1 if total else 0


if __name__ == '__main__':
    sys.exit(main())
