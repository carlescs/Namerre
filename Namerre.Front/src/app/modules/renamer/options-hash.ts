// Pure SHA-256 also works on self-hosted HTTP origins without Web Crypto's subtle API.
export function optionsHash(text: string): string {
  const input = new TextEncoder().encode(text);
  const length = Math.ceil((input.length + 9) / 64) * 64;
  const buffer = new Uint8Array(length);
  buffer.set(input);
  buffer[input.length] = 128;
  const view = new DataView(buffer.buffer);
  view.setUint32(length - 4, input.length * 8);
  const primes: number[] = [];
  for (let candidate = 2; primes.length < 64; candidate++) {
    if (primes.every((prime) => candidate % prime !== 0)) primes.push(candidate);
  }
  const state = primes.slice(0, 8).map((prime) => (Math.sqrt(prime) * 2 ** 32) | 0);
  const constants = primes.map((prime) => (Math.cbrt(prime) * 2 ** 32) | 0);
  const rotate = (value: number, bits: number) => (value >>> bits) | (value << (32 - bits));
  for (let offset = 0; offset < length; offset += 64) {
    const words = new Int32Array(64);
    for (let i = 0; i < 16; i++) words[i] = view.getInt32(offset + i * 4);
    for (let i = 16; i < 64; i++) {
      const a = words[i - 15];
      const b = words[i - 2];
      words[i] =
        (words[i - 16] +
          (rotate(a, 7) ^ rotate(a, 18) ^ (a >>> 3)) +
          words[i - 7] +
          (rotate(b, 17) ^ rotate(b, 19) ^ (b >>> 10))) |
        0;
    }
    let [a, b, c, d, e, f, g, h] = state;
    for (let i = 0; i < 64; i++) {
      const first =
        (h +
          (rotate(e, 6) ^ rotate(e, 11) ^ rotate(e, 25)) +
          ((e & f) ^ (~e & g)) +
          constants[i] +
          words[i]) |
        0;
      const second =
        ((rotate(a, 2) ^ rotate(a, 13) ^ rotate(a, 22)) + ((a & b) ^ (a & c) ^ (b & c))) | 0;
      h = g;
      g = f;
      f = e;
      e = (d + first) | 0;
      d = c;
      c = b;
      b = a;
      a = (first + second) | 0;
    }
    [a, b, c, d, e, f, g, h].forEach((value, i) => (state[i] = (state[i] + value) | 0));
  }
  return state.map((value) => (value >>> 0).toString(16).padStart(8, '0')).join('');
}
