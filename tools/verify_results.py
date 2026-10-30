"""Recompute the published results from immutable, correctness-checked raw data."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics

MODES = ['fp32_pedantic', 'fp16_tensor_fp32_accumulate',
         'bf16_tensor_fp32_accumulate', 'tf32_tensor_fp32_accumulate']
LABELS = ['FP32', 'FP16', 'BF16', 'TF32']
BEFORE = 'cuda-baseline-2026-10-30.json'
AFTER = 'cuda-internal-default-quiet-after-2026-10-30.json'
STABILITY = 'cuda-unlocked-stability-10min-2026-10-30.json'


def require(condition, message):
    if not condition:
        raise ValueError(message)


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def compare(before, after, published):
    for data in (before, after):
        require(data.get('passed') is True, 'A measurement did not pass correctness')
        require(len(data['trials']) == 3, 'Expected three benchmark trials')
        require(len(data['nonconstant_checks']) == 4, 'Missing independent nonconstant checks')
        for check in data['nonconstant_checks']:
            require(check['reference_checked'] and check['all_output_elements_checked'] == 256**2,
                    'Independent correctness check incomplete')
        for trial in data['trials']:
            require([b['name'] for b in trial['benchmarks']] == MODES, 'Benchmark modes differ')
            for check in trial['benchmarks']:
                require(check['n'] == 4096 and check['samples'] == 30 and check['warmup_seconds'] == 2,
                        'Workload parameters differ')
                require(check['reference_checked'] and check['all_output_elements_checked'] == 4096**2,
                        'Full output check incomplete')
                require(len(check['sample_ms']) == 30, 'Missing timing samples')
                ms = statistics.median(check['sample_ms'])
                require(math.isclose(ms, check['median_ms'], rel_tol=1e-10), 'Timing median mismatch')
                require(math.isclose(2*4096**3 / (ms*1e9), check['tflops'], rel_tol=1e-10),
                        'Reported TFLOPS do not match timings')
    for key in ('source_sha256', 'dll_sha256', 'library_versions', 'numpy_version'):
        require(before[key] == after[key], f'Unmatched {key}')
    for key in ('name', 'pci.bus_id', 'pci.device_id', 'pci.sub_device_id',
                'driver_version', 'vbios_version', 'memory.total', 'power.limit'):
        require(before['telemetry_at_start'][key] == after['telemetry_at_start'][key], f'Unmatched GPU setting {key}')
    require([r['mode'] for r in published] == LABELS, 'Published row order differs')
    calculated = []
    for index, mode in enumerate(LABELS):
        old = statistics.median(t['benchmarks'][index]['tflops'] for t in before['trials'])
        new = statistics.median(t['benchmarks'][index]['tflops'] for t in after['trials'])
        gain = (new/old-1)*100
        for key, value in (('before_tflops',old),('after_tflops',new),('gain_percent',gain)):
            require(math.isclose(value, published[index][key], rel_tol=1e-10), f'Published {mode}/{key} mismatch')
        calculated.append({'mode':mode,'before_tflops':old,'after_tflops':new,'gain_percent':gain})
    return calculated


def verify(root):
    results = root / 'results'
    before, after = (load(results/'raw'/p) for p in (BEFORE,AFTER))
    summary = load(results/'verified-session.json')
    rows = compare(before, after, summary['results'])
    source = results/'measurement-source/benchmark-3060ti.py'
    require(hashlib.sha256(source.read_bytes()).hexdigest() == before['source_sha256'], 'Measurement source changed')
    stability = load(results/'raw'/STABILITY)
    require(stability['passed'] and stability['elapsed_seconds'] >= 600, 'Mixed-load check incomplete')
    require(stability['measurement_source_sha256'] == before['source_sha256'], 'Mixed-load measurement source differs')
    require(hashlib.sha256((results/'measurement-source/stability-3060ti.py').read_bytes()).hexdigest() == stability['source_sha256'], 'Mixed-load source changed')
    for key in ('elapsed_seconds','completed_cycles','all_output_elements_checked','max_recorded_temperature_c'):
        require(stability[key] == summary['prior_mixed_compute_check'][key], f'Mixed-load summary mismatch: {key}')
    for cycle in stability['cycles']:
        for check in cycle['checks']:
            require(check['reference_checked'] and check['all_output_elements_checked'] == check['n']**2,
                    'Mixed-load output check incomplete')
    return rows


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parent.parent)
    args=parser.parse_args()
    try:
        rows=verify(args.root)
        for row in rows:
            print(f"{row['mode']:4} {row['before_tflops']:6.2f} -> {row['after_tflops']:6.2f} TFLOPS  +{row['gain_percent']:.1f}%")
        print('PASS: arithmetic, correctness flags, source hashes, matched settings and mixed-load summary.')
        print('This verifies archived data, not current GPU performance or independent certification.')
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(1, f'Results refused: {error}\n')
