"""Bounded mixed CUDA correctness/load check; no tuning, unlock or reboot."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import time

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('gpu_measurement', ROOT / 'benchmark-3060ti.py')
bench = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bench)
SETTINGS = ('name', 'pci.bus_id', 'pci.device_id', 'pci.sub_device_id',
            'driver_version', 'vbios_version', 'memory.total', 'power.limit')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seconds', type=int, default=600, choices=range(60, 601))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    journal = args.output.with_suffix('.jsonl')
    if args.output.exists() or journal.exists():
        parser.error('Refusing to overwrite existing evidence')
    if os.environ.get('NVIDIA_TF32_OVERRIDE'):
        raise RuntimeError('NVIDIA_TF32_OVERRIDE must be unset')
    report = dict(schema_version=1, purpose='bounded_mixed_cuda_correctness_and_load',
                  captured_utc=datetime.now(timezone.utc).isoformat(),
                  target_seconds=args.seconds, passed=False, source_sha256=bench.sha256(Path(__file__)),
                  measurement_source_sha256=bench.sha256(ROOT / 'benchmark-3060ti.py'),
                  cycles=[], all_output_elements_checked=0,
                  max_recorded_temperature_c=0,
                  limitation='Finite compute test only; no sleep, resume, reboot or long-term guarantee.')
    start = time.monotonic()
    cuda = None
    try:
        baseline_path = ROOT / 'cuda-after-clean-unlock-2026-10-30.json'
        after = json.loads(baseline_path.read_text(encoding='utf-8'))
        assert after['passed'], 'Completed unlocked benchmark required'
        report['after_benchmark_sha256'] = bench.sha256(baseline_path)
        initial = bench.telemetry()
        assert all(initial[k] == after['telemetry_at_start'][k] for k in SETTINGS), 'Settings drift'
        assert int(initial['temperature.gpu']) < 85, 'Temperature guard'
        report['telemetry_at_start'] = initial
        assert report['measurement_source_sha256'] == after['source_sha256'], 'Measurement source changed'
        assert all(bench.sha256(ROOT / path) == expected for path, expected in after['dll_sha256'].items()), 'DLL changed'
        cuda = bench.Cuda()
        report['library_versions'] = cuda.versions()
        assert report['library_versions'] == after['library_versions'], 'Library version changed'
        cycle = 0
        while time.monotonic() - start < args.seconds:
            cycle += 1
            row = dict(cycle=cycle, checks=[], telemetry=[])
            for mode in bench.MODES[1:]:
                check = cuda.measure(mode, 4096, 30, 2)
                row['checks'].append(check)
                report['all_output_elements_checked'] += check['all_output_elements_checked']
                current = bench.telemetry()
                row['telemetry'].append(current)
                report['max_recorded_temperature_c'] = max(report['max_recorded_temperature_c'], int(current['temperature.gpu']))
                assert all(current[k] == initial[k] for k in SETTINGS), 'GPU configuration changed'
                assert int(current['temperature.gpu']) < 85, 'Temperature guard stopped test'
            # Distinct matrix dimensions vary the seeded nonconstant inputs;
            # every output is compared exactly to an independent CPU product.
            n = (256, 512, 768, 1024)[(cycle - 1) % 4]
            for mode in bench.MODES:
                check = cuda.measure(mode, n, 1, 0, random_check=True)
                row['checks'].append(check)
                report['all_output_elements_checked'] += check['all_output_elements_checked']
            row['elapsed_seconds'] = time.monotonic() - start
            report['cycles'].append(row)
            with journal.open('a', encoding='utf-8') as stream:
                stream.write(json.dumps(row) + '\n')
            speeds = ', '.join(f'{r["name"].split("_")[0]} {r["tflops"]:.2f}' for r in row['checks'][:3])
            print(f'Cycle {cycle}: {row["elapsed_seconds"]:.1f}s; {speeds} TFLOPS; '
                  f'nonconstant {n}x{n} PASS; max sampled temperature {report["max_recorded_temperature_c"]} C', flush=True)
        report['telemetry_after'] = bench.telemetry()
        assert all(report['telemetry_after'][k] == initial[k] for k in SETTINGS), 'Final settings drift'
        report['passed'] = True
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        if cuda is not None:
            try:
                cuda.call('cublasDestroy_v2', cuda.handle)
            except Exception as error:
                report['passed'] = False
                report['cleanup_error'] = str(error)
        report['elapsed_seconds'] = time.monotonic() - start
        report['completed_cycles'] = len(report['cycles'])
        report['completed_utc'] = datetime.now(timezone.utc).isoformat()
        args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({k: report[k] for k in ('passed', 'elapsed_seconds', 'completed_cycles',
                                               'all_output_elements_checked', 'max_recorded_temperature_c')}), flush=True)
    if not report['passed']:
        raise RuntimeError('Stability test did not pass')


if __name__ == '__main__':
    main()
