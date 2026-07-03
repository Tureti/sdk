import { ReplUnclosedQuoteError, splitQuotedLine } from './splitQuotedLine';

describe('splitQuotedLine', () => {
    it('parses escaped quotes inside double quotes as one argument', () => {
        const line = '"' + 'ab \\"c\\" d' + '"';
        expect(splitQuotedLine(line)).toEqual(['ab "c" d']);
    });

    it('throws on unclosed quote after text', () => {
        expect(() => splitQuotedLine('abc"')).toThrow(ReplUnclosedQuoteError);
        expect(() => splitQuotedLine("abc'")).toThrow(ReplUnclosedQuoteError);
    });

    it('throws on lone opening quote', () => {
        expect(() => splitQuotedLine('"')).toThrow(ReplUnclosedQuoteError);
        expect(() => splitQuotedLine("'")).toThrow(ReplUnclosedQuoteError);
    });

    it('splits unquoted words on whitespace', () => {
        expect(splitQuotedLine('one two  three')).toEqual(['one', 'two', 'three']);
    });

    it('treats adjacent quoted and unquoted segments as one word', () => {
        expect(splitQuotedLine('ab"c d"ef')).toEqual(['abc def']);
        expect(splitQuotedLine("ab'c d'ef")).toEqual(['abc def']);
    });

    it('produces an empty argument for empty quotes', () => {
        expect(splitQuotedLine('a "" b')).toEqual(['a', '', 'b']);
        expect(splitQuotedLine("a '' b")).toEqual(['a', '', 'b']);
    });

    it('parses single-quoted text literally as one argument', () => {
        expect(splitQuotedLine('"ab c d"')).toEqual(['ab c d']);
        expect(splitQuotedLine("'ab c d'")).toEqual(['ab c d']);
    });
});
