// map-config.js
// Cấu hình bản đồ chung cho toàn bộ dự án để đảm bảo chủ quyền biển đảo Việt Nam

const VN_ISLANDS = [
    { lng: 112.00, lat: 16.47, name: 'Quần đảo Hoàng Sa<br><small>(Việt Nam)</small>' },
    { lng: 114.35, lat:  9.40, name: 'Quần đảo Trường Sa<br><small>(Việt Nam)</small>' }
];

const REMOVE_NAMES = [
    // Các từ khoá chung & Tên Quận/Thành phố
    "Sansha", "Nansha District", "Xisha District", "Sansha Shi", "Nansha", "南沙区",
    "南沙群岛", "西沙群岛", "Paracel Islands", "Spratly Islands",
    
    // Cụm Hoàng Sa (Paracel)
    "Yongle Qundao", "Qilianyu", "Yongxing Dao", "Zhongjian Dao", "Zhaoshu Dao",
    "Woody Island", "Triton Island", "Tree Island", "Lincoln Island", "Triton",
    "Xisha", "西沙区", "Bremen Bank", "滨湄滩", "Discovery Reef", "华光礁", 
    "Bombay Reef", "浪花礁", "Macclesfield Bank", "Zhongsha Qundao",
    "Xã khu Triệu Thuật", "赵述社区", "Yongxing", "永兴镇", "Yagong", "鸭公社区",
    "Xã khu Cam Tuyền", "甘泉社区", "Xã khu Linh Dương", "羚羊社区",
    "Xã khu Tấn Khanh", "晋卿社区", "Xã khu", "社区", "镇", "Yongxing Zhen",
    "Yinyu", "银屿社区", "ĐÁ TOÀN PHÚ", "全富礁", "ANTELOPE REEF", "羚羊礁",
    "YONGNAN SHOAL", "永南暗沙", "Xã khu Bắc Đảo", "北岛社区", "North Island",
    "Sansha Yongxing Airport", "三沙永兴机场", "Yongxing Airport", "ILTIS BANK", "银砾滩",
    "OBSERVATION BANK", "银屿礁盘",

    // Cụm Trường Sa (Spratly)
    "Kalayaan", "Taiping Dao", "Itu Aba", "Itu Aba Island", 
    "Zhongye Dao", "Thitu Island", "Pag-asa", "Thitu",
    "Subi Reef", "Zhubi Jiao", "Zhubi", 
    "Mischief Reef", "Meiji Jiao", "Meiji", 
    "Fiery Cross Reef", "Yongshu Jiao", "Yongshu",
    "Cuarteron Reef", "Huayang Jiao", "Huayang",
    "Gaven Reefs", "Nanxun Jiao", "Nanxun",
    "Hughes Reef", "Dongmen Jiao", "Dongmen",
    "Johnson South Reef", "Chigua Jiao", "Chigua",
    "Loaita Island", "Nanshan Island", "Sand Cay", "Sin Cowe Island",
    "Kota", "Panata", "Parola", "Likas", "Lawak", "Patag", "Rizal", "Commodore Reef",
    "Amboyna Cay", "Swallow Reef", "Layang-Layang",
    "COLLINS REEF", "Đá Cô Lin", "chì guā dǎo", "赤瓜岛", "Sinh Tồn", "Sinh Ton"
];

// Hàm cấu hình chung cho MapLibreGL Map (như ở Home/Map và Home/TripDetails)
function applyMapLibreStyleFilter(map) {
    const layers = map.getStyle().layers;
    if (!layers) return;

    layers.forEach(layer => {
        // 1. Làm bản đồ màu sắc hơn (giống Leaflet OSM mặc định)
        if (layer.id.includes('water') && layer.type === 'fill') {
            map.setPaintProperty(layer.id, 'fill-color', '#aad3df');
        }
        if ((layer.id.includes('park') || layer.id.includes('wood') || layer.id.includes('forest')) && layer.type === 'fill') {
            map.setPaintProperty(layer.id, 'fill-color', '#cddfa3');
        }

        // 2. Ẩn toàn bộ text ở vùng biển
        if (
            layer.type === 'symbol' &&
            (
                layer.id.includes('water') ||
                layer.id.includes('marine') ||
                layer.id.includes('ocean') ||
                layer.id.includes('sea')
            )
        ) {
            map.setLayoutProperty(layer.id, 'visibility', 'none');
        }

        // 3. Lọc danh sách tên cụ thể ở tất cả các layer khác
        if (layer.type === 'symbol' && layer.layout && layer.layout['text-field']) {
            let currentFilter = map.getFilter(layer.id);
            let newFilter = ["all"];
            
            if (currentFilter) {
                if (currentFilter[0] === 'all') {
                    newFilter = [...currentFilter];
                } else {
                    newFilter.push(currentFilter);
                }
            }
            
            REMOVE_NAMES.forEach(name => {
                let lowerName = name.toLowerCase();
                newFilter.push(["!=", ["downcase", ["get", "name"]], lowerName]);
                newFilter.push(["!=", ["downcase", ["get", "name_en"]], lowerName]);
            });
            
            map.setFilter(layer.id, newFilter);
        }
    });
}

// Thêm các marker Hoàng Sa, Trường Sa cho MapLibreGL
function addVNIslandsToMapLibre(mapLibreInstance, maplibregl) {
    VN_ISLANDS.forEach(function (island) {
        var el = document.createElement('div');
        el.style.cssText = 'color: #555555; font-weight: bold; font-size: 14px; white-space: nowrap; text-align: center; text-shadow: 1px 1px 2px #fff, -1px -1px 2px #fff; cursor: default; z-index: 1000;';
        el.innerHTML = island.name;
        new maplibregl.Marker({ element: el })
            .setLngLat([island.lng, island.lat])
            .addTo(mapLibreInstance);
    });
}

// Thêm marker Hoàng Sa, Trường Sa cho Leaflet Map
function addVNIslandsToLeaflet(leafletMap, L) {
    VN_ISLANDS.forEach(function (island) {
        var myIcon = L.divIcon({
            className: 'vn-island-label',
            html: `<div style="color: #555555; font-weight: bold; font-size: 14px; white-space: nowrap; text-align: center; text-shadow: 1px 1px 2px #fff, -1px -1px 2px #fff; cursor: default;">${island.name}</div>`,
            iconSize: [120, 40],
            iconAnchor: [60, 20]
        });
        L.marker([island.lat, island.lng], { icon: myIcon, interactive: false, zIndexOffset: 1000 }).addTo(leafletMap);
    });
}
