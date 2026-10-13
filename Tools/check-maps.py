#!/usr/bin/env python3
"""Check the map assets' own format, without opening the Editor.

A vein is written in one place: the `Ore:` list. The `Terrain:` byte under it stays plain, because the
conversion lets a shape patch claim its cells as ore whatever the array says (MapDefinition.StampTerrain).
Three things are wrong in a map file, and every one of them is silent in a run:

  * a `Terrain:` byte that says Ore. With no entry naming the cell, the world still gets an ore tile -
    one only a drill may stand on, and one that yields nothing when it does. With an entry beside it, the
    byte is the half-write that leaves exactly that tile behind the moment the entry is taken away by
    hand;
  * two `- Cell:` entries for one cell. The first entry is the one that counts, here and in the
    conversion alike, so the second is an edit that does nothing;
  * an entry outside the map, or a terrain array that does not describe it.

The rules themselves are MapValidation's (`OreInTerrain`) and MapAsset.Validate's (`DuplicateOre`): they
run in the painting window and in the test suite, and this is the same answers for a file, which is where
a hand edit - a script's, or an agent's - lands before either of those ever sees it.

Usage:  python Tools/check-maps.py [map.asset ...]     (default: every map under Assets/Data/Maps)
"""

import re
import sys
from pathlib import Path

ORIGIN = Path(__file__).resolve().parent.parent
DEFAULT_DIR = ORIGIN / 'Assets' / 'Data' / 'Maps'

CELL = re.compile(r'^\s*- Cell: \{x: (-?\d+), y: (-?\d+)\}')
SHAPE = re.compile(r'^\s*Shape: (\d+)')
TERRAIN = re.compile(r'^  Terrain: ([0-9a-fA-F]*)\s*$')
SIZE = re.compile(r'^  (Width|Height): (-?\d+)\s*$')

GROUND, ORE, ROCK, WATER, VOID = 0, 1, 2, 3, 4
NAMES = {GROUND: 'Ground', ORE: 'Ore', ROCK: 'Rock', WATER: 'Water', VOID: 'Void'}


def read(path):
    """The four things this check needs: the size, the array, and the ore list in written order."""
    width = height = None
    terrain = None
    ore = []
    pending = None

    for line in path.read_text(encoding='utf-8').splitlines():
        size = SIZE.match(line)
        if size:
            if size.group(1) == 'Width':
                width = int(size.group(2))
            else:
                height = int(size.group(2))
            continue

        array = TERRAIN.match(line)
        if array:
            terrain = array.group(1)
            continue

        cell = CELL.match(line)
        if cell:
            pending = (int(cell.group(1)), int(cell.group(2)))
            continue

        shape = SHAPE.match(line)
        if shape and pending is not None:
            ore.append(pending)
            pending = None

    return width, height, terrain, ore


def check(path):
    width, height, terrain, ore = read(path)
    problems = []

    if width is None or height is None:
        return [f'{path.name}: no Width/Height to check against']

    if terrain is not None and len(terrain) != width * height * 2:
        problems.append(f'the terrain array is {len(terrain)} hex digits, and {width}x{height} wants '
                        f'{width * height * 2} - the size and the ground disagree')

    seen = set()
    for (x, y) in ore:
        if not (0 <= x < width and 0 <= y < height):
            problems.append(f'the ore list names ({x},{y}), which is off the {width}x{height} map')
            continue

        if (x, y) in seen:
            problems.append(f'the ore list names ({x},{y}) twice: only the first entry counts, so the '
                            f'mineral named after it is ignored')
            continue

        seen.add((x, y))

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
            print('  ok - the ore list is the only place a vein is written')

    print()
    print(f'{len(paths)} map(s), {total} problem(s)')
    return 1 if total else 0


if __name__ == '__main__':
    sys.exit(main())
