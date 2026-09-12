INSERT INTO map(
    map_id, map_key, display_name, width, height, cell_size_m, crs,
    origin_easting_m, origin_northing_m, extent_width_m, extent_height_m,
    geographic_status, source_path
) VALUES
    (0, 'fulda_gap', 'Fulda Gap A2 Test Map', 20, 15, 500, 'abstract',
        NULL, NULL, NULL, NULL, 'abstract', 'data/maps/fulda_gap.json'),
    (1, 'point_alpha_corridor', 'Point Alpha - Huenfeld Corridor', 92, 80,
        250, 'ETRS89 / UTM zone 32N (EPSG:25832)', 554000, 5610000, 20000, 20000,
        'feature_draft', 'data/maps/point_alpha_cells.csv');

INSERT INTO map_source(
    map_source_id, map_id, source_key, display_name, source_url,
    license_name, attribution, source_date, notes
) VALUES (
    0, 1, 'bkg_dgm200', 'BKG Digital Terrain Model DGM200',
    'https://gdz.bkg.bund.de/index.php/default/digitale-geodaten/digitale-gelandemodelle/digitales-gelandemodell-gitterweite-200-m-dgm200.html',
    'Datenlizenz Deutschland - Namensnennung - Version 2.0',
    'GeoBasis-DE / BKG 2020 (data modified)', '2019-12-31',
    'Official 200 m elevations sampled into 250 m flat-top game hexes.'
), (
    1, 1, 'bkg_dlm250', 'BKG Digital Landscape Model DLM250',
    'https://gdz.bkg.bund.de/index.php/default/webdienste/digitale-landschaftsmodelle/wfs-digitales-landschaftsmodell-1-250-000-wfs-dlm250.html',
    'Datenlizenz Deutschland - Namensnennung - Version 2.0',
    'GeoBasis-DE / BKG 2025 (data modified)', '2025-12-31',
    'Modern roads, waterways, settlements, woodland, marsh, rough ground, water, and transport structures clipped through the official WFS; requires 1985 review.'
);
