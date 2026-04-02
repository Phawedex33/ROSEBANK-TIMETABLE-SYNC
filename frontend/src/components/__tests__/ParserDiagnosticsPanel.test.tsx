import { render, screen } from '@testing-library/react';
import { ParserDiagnosticsPanel } from '../ParserDiagnosticsPanel';

describe('ParserDiagnosticsPanel', () => {
  it('renders diagnostic rows with confidence levels', () => {
    render(
      <ParserDiagnosticsPanel
        rows={[
          {
            id: 'row-1',
            raw: 'Monday 09:00-10:00 Mathematics',
            parsed: {
              day: 'Monday',
              time: '09:00-10:00',
              module: 'Mathematics',
            },
            parserBranch: 'SimpleLineParser',
            confidence: 0.95,
          },
        ]}
      />,
    );

    expect(screen.getByText(/Parser confidence snapshot/i)).toBeInTheDocument();
    expect(screen.getByText(/95%/i)).toBeInTheDocument();
    expect(screen.getByText(/Module: Mathematics/i)).toBeInTheDocument();
  });
});
