"""Ordinary CUDA/cuBLAS work on this PC. Does not unlock or change GPU settings.

Uses the project's 4096/30-sample/2-second-warmup GEMM workload, with full
all-ones output checks plus a separate small nonconstant matrix check. This is
a separate local runner, not an input to the upstream compare_results.py tool.
"""
import argparse
import ctypes as ct
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import statistics
import subprocess
import time

import numpy as np

ROOT = Path(__file__).resolve().parent
MODES = ('fp32_pedantic', 'fp16_tensor_fp32_accumulate',
         'bf16_tensor_fp32_accumulate', 'tf32_tensor_fp32_accumulate')


def telemetry():
    fields = ('name', 'pci.bus_id', 'pci.device_id', 'pci.sub_device_id', 'driver_version',
              'vbios_version', 'memory.total', 'memory.used', 'utilization.gpu',
              'temperature.gpu', 'power.limit', 'clocks.sm', 'clocks.mem')
    result = subprocess.run(['nvidia-smi', '--query-gpu=' + ','.join(fields),
                             '--format=csv,noheader,nounits'], check=True,
                            capture_output=True, text=True, timeout=15)
    rows = [line.strip().split(', ') for line in result.stdout.strip().splitlines()]
    if len(rows) != 1:
        raise RuntimeError('Expected exactly one NVIDIA GPU')
    row = dict(zip(fields, rows[0], strict=True))
    if (row['name'] != 'NVIDIA GeForce RTX 3060 Ti'
            or row['pci.device_id'].lower() != '0x248910de'
            or row['pci.bus_id'].lower() != '00000000:05:00.0'):
        raise RuntimeError('This runner is pinned to RTX 3060 Ti 2489 at bus 05')
    return row


def load_libraries():
    runtime_path, = (ROOT / 'cuda12.6/cuda_cudart').rglob('cudart64_12.dll')
    blas_path, = (ROOT / 'cuda12.6/libcublas').rglob('cublas64_12.dll')
    directories = [os.add_dll_directory(str(path)) for path in
                   {runtime_path.parent, blas_path.parent}]
    return ct.WinDLL(str(runtime_path)), ct.WinDLL(str(blas_path)), directories


class Cuda:
    def __init__(self):
        self.runtime, self.blas, self.dll_directories = load_libraries()
        pointer, integer = ct.c_void_p, ct.c_int
        signatures = {
            'cudaSetDevice': [integer], 'cudaDeviceSynchronize': [],
            'cudaRuntimeGetVersion': [ct.POINTER(integer)],
            'cudaDriverGetVersion': [ct.POINTER(integer)],
            'cudaMalloc': [ct.POINTER(pointer), ct.c_size_t], 'cudaFree': [pointer],
            'cudaMemcpy': [pointer, pointer, ct.c_size_t, integer],
            'cudaEventCreate': [ct.POINTER(pointer)], 'cudaEventDestroy': [pointer],
            'cudaEventRecord': [pointer, pointer], 'cudaEventSynchronize': [pointer],
            'cudaEventElapsedTime': [ct.POINTER(ct.c_float), pointer, pointer],
        }
        for name, arguments in signatures.items():
            function = getattr(self.runtime, name)
            function.restype, function.argtypes = integer, arguments
        self.runtime.cudaGetErrorString.restype = ct.c_char_p
        self.runtime.cudaGetErrorString.argtypes = [integer]
        signatures = {
            'cublasCreate_v2': [ct.POINTER(pointer)], 'cublasDestroy_v2': [pointer],
            'cublasGetVersion_v2': [pointer, ct.POINTER(integer)],
            'cublasSetMathMode': [pointer, integer],
            'cublasSgemm_v2': [pointer, integer, integer, integer, integer, integer,
                               pointer, pointer, integer, pointer, integer,
                               pointer, pointer, integer],
            'cublasGemmEx': [pointer, integer, integer, integer, integer, integer,
                             pointer, pointer, integer, integer, pointer, integer,
                             integer, pointer, pointer, integer, integer, integer, integer],
        }
        for name, arguments in signatures.items():
            function = getattr(self.blas, name)
            function.restype, function.argtypes = integer, arguments
        self.call('cudaSetDevice', 0)
        self.handle = pointer()
        self.call('cublasCreate_v2', ct.byref(self.handle))

    def call(self, name, *arguments):
        library = self.runtime if name.startswith('cuda') else self.blas
        status = getattr(library, name)(*arguments)
        if status:
            message = (self.runtime.cudaGetErrorString(status).decode()
                       if name.startswith('cuda') else f'cuBLAS status {status}')
            raise RuntimeError(f'{name}: {message}')

    def versions(self):
        result = {}
        for label, function, arguments in (
            ('cuda_runtime', 'cudaRuntimeGetVersion', []),
            ('cuda_driver_api', 'cudaDriverGetVersion', []),
            ('cublas', 'cublasGetVersion_v2', [self.handle]),
        ):
            value = ct.c_int()
            self.call(function, *arguments, ct.byref(value))
            result[label] = value.value
        return result

    def measure(self, mode, n, samples, warmup, random_check=False):
        kind = MODES.index(mode)
        if random_check:
            rng = np.random.default_rng(2489)
            host_a = np.asfortranarray(rng.integers(-4, 5, (n, n)).astype(np.float32) / 4)
            host_b = np.asfortranarray(rng.integers(-4, 5, (n, n)).astype(np.float32) / 4)
            reference = host_a @ host_b
        else:
            host_a = np.ones((n, n), dtype=np.float32, order='F')
            host_b = np.ones((n, n), dtype=np.float32, order='F')
            reference = n
        if kind == 1:
            host_a, host_b = (np.asfortranarray(array.astype(np.float16)) for array in (host_a, host_b))
        elif kind == 2:
            host_a, host_b = (np.asfortranarray((array.view(np.uint32) >> 16).astype(np.uint16))
                              for array in (host_a, host_b))
        output = np.empty((n, n), dtype=np.float32, order='F')
        allocations, events = [], []
        try:
            for array in (host_a, host_b, output):
                device = ct.c_void_p()
                self.call('cudaMalloc', ct.byref(device), array.nbytes)
                allocations.append(device)
            a, b, c = allocations
            self.call('cudaMemcpy', a, host_a.ctypes.data, host_a.nbytes, 1)
            self.call('cudaMemcpy', b, host_b.ctypes.data, host_b.nbytes, 1)
            self.call('cublasSetMathMode', self.handle, (2, 1, 1, 3)[kind])
            alpha, beta = ct.c_float(1), ct.c_float(0)

            def gemm():
                if kind == 0:
                    self.call('cublasSgemm_v2', self.handle, 0, 0, n, n, n,
                              ct.byref(alpha), a, n, b, n, ct.byref(beta), c, n)
                else:
                    data_type = (0, 2, 14, 0)[kind]
                    compute_type = 77 if kind == 3 else 68
                    self.call('cublasGemmEx', self.handle, 0, 0, n, n, n,
                              ct.byref(alpha), a, data_type, n, b, data_type, n,
                              ct.byref(beta), c, 0, n, compute_type, 99)

            started = time.perf_counter()
            while True:
                gemm()
                self.call('cudaDeviceSynchronize')
                if time.perf_counter() - started >= warmup:
                    break
            for _ in range(2):
                event = ct.c_void_p()
                self.call('cudaEventCreate', ct.byref(event))
                events.append(event)
            start, stop = events
            milliseconds = []
            for _ in range(samples):
                self.call('cudaEventRecord', start, None)
                gemm()
                self.call('cudaEventRecord', stop, None)
                self.call('cudaEventSynchronize', stop)
                elapsed = ct.c_float()
                self.call('cudaEventElapsedTime', ct.byref(elapsed), start, stop)
                milliseconds.append(elapsed.value)
            self.call('cudaMemcpy', output.ctypes.data, c, output.nbytes, 2)
            if not np.all(np.isfinite(output)) or not np.all(output == reference):
                raise RuntimeError(f'{mode}: full output does not match the exact reference')
            median = statistics.median(milliseconds)
            return {'name': mode, 'n': n, 'samples': samples, 'warmup_seconds': warmup,
                    'median_ms': median, 'tflops': 2 * n ** 3 / (median * 1e9),
                    'all_output_elements_checked': n * n, 'reference_checked': True,
                    'input': 'seeded_quarter_integers' if random_check else 'ones',
                    'sample_ms': milliseconds}
        finally:
            for event in events:
                self.call('cudaEventDestroy', event)
            for device in allocations:
                self.call('cudaFree', device)


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as source:
        while chunk := source.read(4 * 1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    arguments = parser.parse_args()
    if arguments.output.exists():
        parser.error('Refusing to overwrite existing measurement')
    if os.environ.get('NVIDIA_TF32_OVERRIDE'):
        raise RuntimeError('NVIDIA_TF32_OVERRIDE must be empty for this protocol')
    report = {'schema_version': 1, 'captured_utc': datetime.now(timezone.utc).isoformat(),
              'purpose': 'ordinary_cuda_baseline_not_unlock_validation',
              'unlock_attempted': False, 'passed': False, 'trials': [],
              'source_sha256': sha256(Path(__file__)), 'numpy_version': np.__version__}
    arguments.output.parent.mkdir(exist_ok=True, parents=True)
    cuda = None
    try:
        report['telemetry_before'] = telemetry()
        print('Waiting up to 60 seconds for <=5% GPU use, <=2048 MiB used, <=65 C.', flush=True)
        idle_deadline = time.monotonic() + 60
        while True:
            before = telemetry()
            if (int(before['utilization.gpu']) <= 5 and int(before['memory.used']) <= 2048
                    and int(before['temperature.gpu']) <= 65):
                break
            if time.monotonic() >= idle_deadline:
                raise RuntimeError('GPU remained busy; pause other GPU workloads and rerun')
            time.sleep(2)
        report['telemetry_at_start'] = before
        report['dll_sha256'] = {str(path.relative_to(ROOT)): sha256(path)
                                for path in (ROOT / 'cuda12.6').rglob('*.dll')}
        cuda = Cuda()
        report['library_versions'] = cuda.versions()
        if report['library_versions']['cuda_runtime'] != 12060:
            raise RuntimeError('Expected pinned CUDA runtime 12.6')
        report['nonconstant_checks'] = []
        for mode in MODES:
            result = cuda.measure(mode, 256, 1, 0, random_check=True)
            report['nonconstant_checks'].append(result)
            print(f'{mode}: nonconstant full-matrix correctness PASS', flush=True)
        for trial in range(1, 4):
            row = {'trial': trial, 'benchmarks': [], 'telemetry': []}
            report['trials'].append(row)
            for mode in MODES:
                result = cuda.measure(mode, 4096, 30, 2)
                row['benchmarks'].append(result)
                current = telemetry()
                row['telemetry'].append(current)
                print(f'Trial {trial} {mode}: {result["tflops"]:.3f} TFLOPS; full output PASS', flush=True)
                if (int(current['temperature.gpu']) >= 85
                        or current['driver_version'] != before['driver_version']
                        or current['power.limit'] != before['power.limit']):
                    raise RuntimeError('Temperature or configuration guard stopped the benchmark')
        report['median_tflops'] = {
            mode: statistics.median(trial['benchmarks'][index]['tflops'] for trial in report['trials'])
            for index, mode in enumerate(MODES)}
        report['telemetry_after'] = telemetry()
        report['passed'] = True
        print(json.dumps(report['median_tflops'], indent=2), flush=True)
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        if cuda is not None:
            cuda.call('cublasDestroy_v2', cuda.handle)
        arguments.output.write_text(json.dumps(report, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
