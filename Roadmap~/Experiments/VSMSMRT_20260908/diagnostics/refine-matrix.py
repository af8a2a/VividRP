from pathlib import Path
p=Path('Editor/Tools/VSMSMRTProbe.cs');s=p.read_text();s=s.replace('        private int VariantIndex => Mathf.Max(0, m_Stage) / 4;','''        private static readonly int[] VariantOrder = { 0, 1, 2, 3, 4, 5, 0, 2, 0, 2, 0, 2 };
        private static readonly int[] ScenarioOrder = { 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3 };
        private int VariantIndex => VariantOrder[m_Stage];''')
s=s.replace('private int StageLength => m_Stage < 0 ? 2 : m_Stage % 4 == 0 ? 32 : m_Stage % 4 == 1 ? 96 : 128;','private int StageLength => Scenario == "static" ? 32 : Scenario == "translate" ? 96 : 128;')
s=s.replace('Scenarios[Mathf.Max(0, m_Stage) % 4]','Scenarios[ScenarioOrder[m_Stage]]').replace('if (++m_Stage < Variants.Length * 4)','if (++m_Stage < VariantOrder.Length)');p.write_text(s)
