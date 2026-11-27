import { describe, expect, it } from 'vitest';
import { describeError } from '../../src/errors.js';

describe('describeError', () => {
  it('uses the message', () => {
    expect(describeError(new Error('connection refused'))).toBe('connection refused');
  });

  // What Node raises when every address of a host such as localhost refuses.
  it('lists the causes of an AggregateError without a message', () => {
    const error = new AggregateError([new Error('connect ECONNREFUSED ::1:5432'), new Error('connect ECONNREFUSED 127.0.0.1:5432')], '');
    expect(describeError(error)).toBe('connect ECONNREFUSED ::1:5432; connect ECONNREFUSED 127.0.0.1:5432');
  });

  it('falls back to the error code, then to the error name', () => {
    expect(describeError(Object.assign(new Error(''), { code: 'ECONNREFUSED' }))).toBe('ECONNREFUSED');
    expect(describeError(new TypeError(''))).toBe('TypeError');
  });
});
