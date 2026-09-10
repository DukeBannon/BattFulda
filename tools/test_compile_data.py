import unittest

import compile_data


class CompiledDataTests(unittest.TestCase):
    def test_map_header_and_payload_size(self) -> None:
        result = compile_data.compile_map()
        self.assertEqual(result[:2], bytes((compile_data.MAP_WIDTH, compile_data.MAP_HEIGHT)))
        self.assertEqual(len(result), 2 + compile_data.MAP_WIDTH * compile_data.MAP_HEIGHT)
        self.assertTrue(all(tile <= 4 for tile in result[2:]))

    def test_unit_records_are_compact_and_in_bounds(self) -> None:
        result = compile_data.compile_units()
        self.assertEqual(len(result), 1 + result[0] * 5)
        for offset in range(1, len(result), 5):
            self.assertLess(result[offset + 2], compile_data.MAP_WIDTH)
            self.assertLess(result[offset + 3], compile_data.MAP_HEIGHT)
            self.assertLessEqual(result[offset + 4], 15)

    def test_scenario_matches_compiled_units(self) -> None:
        units = compile_data.compile_units()
        scenario = compile_data.compile_scenario(units[0])
        self.assertEqual(scenario[0], 1)
        self.assertEqual(scenario[3], units[0])


if __name__ == "__main__":
    unittest.main()
