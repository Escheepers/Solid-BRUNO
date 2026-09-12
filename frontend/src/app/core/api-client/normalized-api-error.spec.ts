import { fieldFromType } from './normalized-api-error';

describe('fieldFromType', () => {
  it('recovers a multi-word kebab-case field name as camelCase', () => {
    expect(fieldFromType('urn:bruno:vehicle:registration-number')).toBe('registrationNumber');
  });

  it('recovers a single-word field name unchanged', () => {
    expect(fieldFromType('urn:bruno:vehicle:year')).toBe('year');
  });

  it('recovers a field name from a different entity the same way', () => {
    expect(fieldFromType('urn:bruno:customer:email-address')).toBe('emailAddress');
  });

  it('returns undefined for a type with no trailing segment', () => {
    expect(fieldFromType('urn:bruno:vehicle:')).toBeUndefined();
  });

  it('returns undefined for an empty string', () => {
    expect(fieldFromType('')).toBeUndefined();
  });
});
