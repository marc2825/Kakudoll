#!/usr/bin/env python3
"""Run Gate 1E in an existing isolated Windows test copy, never the source project."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time


# Unity Hubの既定インストール先（WSLから見たWindows側）。環境が違う場合は UNITY_EXECUTABLE か --unity で指定する。
DEFAULT_UNITY = '/mnt/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe'
# WSLのWindows PowerShell。PATHにあればそれを使う（benchmarkのメモリ採取とtimeout時の停止だけで使用）。
POWERSHELL = shutil.which('powershell.exe') or '/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--source', type=Path, default=None,
                        help='VLat repository to copy Packages/schema from (default: this script\'s repository); '
                             'e.g. a clean main checkout for same-day baselines')
    parser.add_argument('--unity', default=os.environ.get('UNITY_EXECUTABLE', DEFAULT_UNITY),
                        help='Unity 2022.3.62f3 executable (default: $UNITY_EXECUTABLE or the Unity Hub default path seen from WSL)')
    parser.add_argument('--label', required=True)
    parser.add_argument('--mode', choices=['EditMode', 'PlayMode', 'benchmark', 'avatar-seed', 'avatar-restore', 'ui-snapshot'], required=True)
    parser.add_argument('--avatar', type=Path)
    parser.add_argument('--filter')
    parser.add_argument('--profile', choices=['all','small','medium','large','custom'], default='all')
    parser.add_argument('--trials', type=int, default=8)
    parser.add_argument('--cycles', type=int, default=60)
    parser.add_argument('--width', type=int, default=128)
    parser.add_argument('--height', type=int, default=64)
    parser.add_argument('--renderers', type=int, default=2)
    parser.add_argument('--keys', type=int, default=3)
    parser.add_argument('--selection-pixels', type=int, default=10)
    parser.add_argument('--state-name', default='VLat1EAvatar')
    parser.add_argument('--timeout-seconds', type=int, default=900)
    parser.add_argument('--trace-undo', action='store_true')
    parser.add_argument('--breakdown', action='store_true',
                        help='diagnostic only: record per-phase VLat Editor timings; do not use as final timing run')
    args = parser.parse_args()
    source = (args.source or Path(__file__).resolve().parents[1]).resolve()
    if not (source/'Packages/jp.vlat.core/package.json').is_file():
        parser.error('--source must be a VLat repository root.')
    stage = args.stage.resolve()
    if stage == source or source in stage.parents or not (stage/'VLatProject/ProjectSettings/ProjectVersion.txt').exists():
        parser.error('Use an existing isolated Windows staging copy outside the repository.')
    if any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_' for c in args.label+args.state_name):
        parser.error('label/state-name must be plain filename stems')
    if args.trials < 2 or args.cycles < 1 or min(args.width,args.height)<3 or args.width*args.height>200000 or not 1<=args.renderers<=16 or not 2<=args.keys<=16 or not 1<=args.selection_pixels<=256:
        parser.error('Invalid benchmark dimensions/counts (max 200000 vertices, 16 renderers/keys).')
    result = stage/'results'/args.label
    if result.with_suffix('.log').exists():
        parser.error('Result label already exists; choose a new label to retain prior evidence.')
    # stageのPackages/schemaをsourceと完全一致させる。過去runの別branch専用fileが残ると、
    # 別sourceとの混在でcompileできない・測定対象が変わるため（2026-11-20に発生）。
    removed = []
    for name in ('Packages', 'schema'):
        shutil.copytree(source/name, stage/name, dirs_exist_ok=True)
        expected = {p.relative_to(source/name) for p in (source/name).rglob('*')}
        for path in sorted((stage/name).rglob('*'), key=lambda p: len(p.parts), reverse=True):
            relative = path.relative_to(stage/name)
            if relative in expected:
                continue
            if path.is_file() or path.is_symlink():
                path.unlink(); removed.append(str(Path(name)/relative))
            elif path.is_dir() and not any(path.iterdir()):
                path.rmdir(); removed.append(str(Path(name)/relative)+'/')
    result.with_suffix('.stage.json').write_text(json.dumps({'source': str(source), 'removedStaleEntries': removed}, indent=2)+'\n')
    inputs = {str(p.relative_to(source)):hashlib.sha256(p.read_bytes()).hexdigest()
              for name in ('Packages','schema') for p in (source/name).rglob('*') if p.is_file()}
    result.with_suffix('.source.json').write_text(json.dumps(inputs,indent=2)+'\n')
    def win(path):
        return subprocess.check_output(['wslpath', '-w', str(path)], text=True).strip()
    cmd = [args.unity, '-batchmode', '-force-d3d11', '-projectPath', win(stage/'VLatProject'), '-logFile', win(result.with_suffix('.log'))]
    if args.mode in ('EditMode', 'PlayMode'):
        cmd += ['-runTests', '-testPlatform', args.mode, '-testResults', win(result.with_suffix('.xml'))]
        if args.filter:
            cmd += ['-testFilter', args.filter]
    else:
        method = {'benchmark': 'Benchmark', 'avatar-seed': 'AvatarSeed', 'avatar-restore': 'AvatarRestore', 'ui-snapshot': 'UiSnapshot'}[args.mode]
        cmd += ['-executeMethod', 'VLat.Tests.Edit.VlatEditor1EProbe.'+method,
                '-vlat1eOutput', win(stage/'results'/(args.label+'-data')),
                '-vlat1eState', win(stage/'VLatProject/Library'/args.state_name),
                '-vlat1eProfile', args.profile, '-vlat1eTrials', str(args.trials), '-vlat1eCycles', str(args.cycles),
                '-vlat1eWidth',str(args.width),'-vlat1eHeight',str(args.height),'-vlat1eRenderers',str(args.renderers),
                '-vlat1eKeys',str(args.keys),'-vlat1eSelectionPixels',str(args.selection_pixels)]
        if args.trace_undo:cmd+=['-vlat1eTraceUndo','1']
        if args.breakdown:cmd+=['-vlat1eBreakdown','1']
        if args.mode.startswith('avatar-'):
            if not args.avatar or not args.avatar.is_file():
                parser.error('Supply the previously rights-checked VRM1 file via --avatar.')
            target = stage/'ExternalAssets'/args.avatar.name
            target.parent.mkdir(exist_ok=True)
            shutil.copyfile(args.avatar, target)
            digest = hashlib.sha256(args.avatar.read_bytes()).hexdigest()
            if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
                raise RuntimeError('Avatar copy changed')
            cmd += ['-vlat1eAvatar', win(target), '-vlat1eAvatarHash', digest]
    began = time.time()
    process = subprocess.Popen(cmd)
    os_samples = []
    while process.poll() is None:
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            if args.mode == 'benchmark':
                sample = subprocess.run([POWERSHELL,
                    '-NoProfile','-Command','Get-Process Unity -ErrorAction SilentlyContinue | Select-Object Id,CPU,WorkingSet64,PrivateMemorySize64 | ConvertTo-Json -Compress'],
                    capture_output=True,text=True)
                if sample.returncode == 0 and sample.stdout.strip():
                    os_samples.append({'elapsedSeconds':time.time()-began,'processes':json.loads(sample.stdout)})
                    result.with_suffix('.os-memory.json').write_text(json.dumps(os_samples,indent=2)+'\n')
            if time.time()-began > args.timeout_seconds:
                partial=stage/'results'/(args.label+'-data')/'partial.json'
                if not partial.exists():
                    raise RuntimeError('Timeout before probe PID was recorded; inspect Unity process manually.')
                state=json.loads(partial.read_text());pid=int(state['process'])
                result.with_suffix('.timeout.json').write_text(json.dumps({'status':'FAIL','reason':'measurement timeout',
                    'elapsedSeconds':time.time()-began,'phase':state.get('currentPhase'),'process':pid},indent=2)+'\n')
                subprocess.run([POWERSHELL,'-NoProfile','-Command',
                    f"Get-Process -Id {pid} -ErrorAction Stop | Where-Object ProcessName -eq 'Unity' | Stop-Process"],check=True)
                process.wait(timeout=30)
    metadata = {'command': cmd, 'exitCode': process.returncode, 'elapsedSeconds': time.time()-began,
                'sourceCommit': subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD'],text=True).strip()}
    result.with_suffix('.exit.json').write_text(json.dumps(metadata,indent=2)+'\n')
    print(json.dumps(metadata))
    raise SystemExit(process.returncode)


if __name__ == '__main__':
    main()
