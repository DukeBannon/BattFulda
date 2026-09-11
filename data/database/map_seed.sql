INSERT INTO map(
    map_id, map_key, display_name, width, height, cell_size_m, crs,
    origin_easting_m, origin_northing_m, geographic_status, source_path
) VALUES
    (0, 'fulda_gap', 'Fulda Gap A2 Test Map', 20, 15, 500, 'abstract',
        NULL, NULL, 'abstract', 'data/maps/fulda_gap.json'),
    (1, 'point_alpha_corridor', 'Point Alpha - Huenfeld Corridor', 40, 40,
        500, 'ETRS89 / UTM zone 32N (EPSG:25832)', 554000, 5610000,
        'elevation_draft', 'data/maps/point_alpha_cells.csv');

INSERT INTO map_source(
    map_source_id, map_id, source_key, display_name, source_url,
    license_name, attribution, source_date, notes
) VALUES (
    0, 1, 'bkg_dgm200', 'BKG Digital Terrain Model DGM200',
    'https://gdz.bkg.bund.de/index.php/default/digitale-geodaten/digitale-gelandemodelle/digitales-gelandemodell-gitterweite-200-m-dgm200.html',
    'Datenlizenz Deutschland - Namensnennung - Version 2.0',
    'GeoBasis-DE / BKG 2020 (data modified)', '2019-12-31',
    'Official 200 m elevations aggregated into 500 m game cells.'
);
