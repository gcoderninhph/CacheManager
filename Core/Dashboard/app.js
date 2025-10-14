(() => {
    const refreshButton = document.getElementById("refresh");
    const tableBody = document.getElementById("cache-table");
    const tabs = document.querySelectorAll(".tab");
    const navContent = document.getElementById("nav-content");
    const navList = document.getElementById("nav-list");
    const contentTitle = document.getElementById("content-title");
    const contentSubtitle = document.getElementById("content-subtitle");
    const searchBox = document.getElementById("search-box");
    const prevButton = document.getElementById("prev-page");
    const nextButton = document.getElementById("next-page");
    const pageInfo = document.getElementById("page-info");

    if (!refreshButton || !tableBody || !tabs.length) {
        return;
    }

    let cachedRegistry = { maps: [], buckets: [] };
    let currentTab = 'map';
    let currentMapName = null;
    let currentPage = 1;
    let currentSearch = '';
    let totalPages = 1;
    let searchTimeout = null;

    // Fetch registry data from API
    const fetchRegistry = async () => {
        try {
            const response = await fetch('./api/registry');
            if (response.ok) {
                cachedRegistry = await response.json();
                renderNavList();
            }
        } catch (error) {
            console.error('Failed to fetch registry:', error);
        }
    };

    // Fetch map data
    const fetchMapData = async (mapName, page = 1, search = '') => {
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '20'
            });
            
            if (search) {
                params.append('search', search);
            }
            
            const response = await fetch(`./api/map/${encodeURIComponent(mapName)}?${params}`);
            if (response.ok) {
                const result = await response.json();
                
                // New format: { mapName, data: { entries, currentPage, pageSize, totalCount, totalPages, hasNext, hasPrev } }
                const pagedData = result.data || {};
                const entries = pagedData.entries || [];
                
                renderMapTable(entries);
                
                // Update pagination state
                currentPage = pagedData.currentPage || 1;
                totalPages = pagedData.totalPages || 1;
                
                updatePagination({
                    currentPage: currentPage,
                    totalPages: totalPages,
                    hasNext: pagedData.hasNext || false,
                    hasPrev: pagedData.hasPrev || false
                });
                
                contentTitle.textContent = `Map: ${mapName}`;
                contentSubtitle.textContent = `${pagedData.totalCount || 0} entries (Page ${currentPage} of ${totalPages})`;
            }
        } catch (error) {
            console.error('Failed to fetch map data:', error);
            tableBody.innerHTML = '<tr><td colspan="4" style="text-align: center; color: var(--danger);">Error loading map data</td></tr>';
        }
    };

    // Update pagination UI
    const updatePagination = (pagination) => {
        if (prevButton && nextButton && pageInfo) {
            prevButton.disabled = !pagination.hasPrev;
            nextButton.disabled = !pagination.hasNext;
            pageInfo.textContent = `Page ${pagination.currentPage} of ${pagination.totalPages}`;
        }
    };

    // Render nav list (maps or buckets)
    const renderNavList = () => {
        if (!navList) return;

        navList.innerHTML = "";
        const items = currentTab === 'map' ? cachedRegistry.maps : cachedRegistry.buckets;

        if (items.length === 0) {
            navList.innerHTML = '<li style="text-align: center; opacity: 0.5;">No items registered</li>';
            return;
        }

        items.forEach((item, index) => {
            const li = document.createElement("li");
            li.textContent = item;
            li.dataset.name = item;
            
            if (index === 0 && !currentMapName) {
                li.classList.add('active');
                currentMapName = item;
                if (currentTab === 'map') {
                    currentPage = 1;
                    currentSearch = '';
                    if (searchBox) searchBox.value = '';
                    fetchMapData(item, 1, '');
                }
            } else if (item === currentMapName) {
                li.classList.add('active');
            }

            li.addEventListener('click', () => {
                navList.querySelectorAll('li').forEach(l => l.classList.remove('active'));
                li.classList.add('active');
                currentMapName = item;
                currentPage = 1;
                currentSearch = '';
                if (searchBox) searchBox.value = '';
                
                if (currentTab === 'map') {
                    fetchMapData(item, 1, '');
                }
            });

            navList.appendChild(li);
        });

        // Update nav content header
        if (navContent) {
            const h2 = navContent.querySelector('h2');
            const p = navContent.querySelector('p');
            if (currentTab === 'map') {
                h2.textContent = 'Maps Registry';
                p.textContent = 'Select a map to view its data.';
            } else if (currentTab === 'bucket') {
                h2.textContent = 'Buckets Registry';
                p.textContent = 'Select a bucket to view its data.';
            }
        }
    };

    // Render map table
    const renderMapTable = (entries) => {
        tableBody.innerHTML = "";
        
        if (entries.length === 0) {
            tableBody.innerHTML = '<tr><td colspan="4" style="text-align: center; opacity: 0.5;">No data in this map</td></tr>';
            return;
        }

        entries.forEach(entry => {
            const tr = document.createElement("tr");
            tr.innerHTML = `
                <td><span class="tag">${entry.key || ''}</span></td>
                <td>${entry.value || ''}</td>
                <td><span style="color: var(--info); font-size: 0.9em;">${entry.lastModified || 'N/A'}</span></td>
                <td><code style="font-size: 0.85em; opacity: 0.7;">${entry.version || 'N/A'}</code></td>
            `;
            tableBody.appendChild(tr);
        });
    };

    tabs.forEach(tab => {
        tab.addEventListener("click", () => {
            tabs.forEach(t => t.classList.remove("active"));
            tab.classList.add("active");
            currentTab = tab.dataset.tab;
            currentMapName = null;
            currentPage = 1;
            currentSearch = '';
            if (searchBox) searchBox.value = '';
            renderNavList();
            
            // Clear table when switching tabs
            tableBody.innerHTML = '<tr><td colspan="3" style="text-align: center; opacity: 0.5;">Select an item from the list</td></tr>';
        });
    });

    // Search functionality
    if (searchBox) {
        searchBox.addEventListener('input', (e) => {
            const searchValue = e.target.value.trim();
            
            // Debounce search
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(() => {
                if (currentMapName && currentTab === 'map') {
                    currentSearch = searchValue;
                    currentPage = 1; // Reset to first page on new search
                    fetchMapData(currentMapName, 1, currentSearch);
                }
            }, 300); // Wait 300ms after user stops typing
        });
    }

    // Pagination controls
    if (prevButton) {
        prevButton.addEventListener('click', () => {
            if (currentPage > 1 && currentMapName) {
                fetchMapData(currentMapName, currentPage - 1, currentSearch);
            }
        });
    }

    if (nextButton) {
        nextButton.addEventListener('click', () => {
            if (currentPage < totalPages && currentMapName) {
                fetchMapData(currentMapName, currentPage + 1, currentSearch);
            }
        });
    }

    refreshButton.addEventListener("click", async () => {
        tableBody.classList.add("pulse");
        await fetchRegistry();
        if (currentMapName && currentTab === 'map') {
            await fetchMapData(currentMapName, currentPage, currentSearch);
        }
        window.setTimeout(() => tableBody.classList.remove("pulse"), 400);
    });

    // Initial load
    fetchRegistry();
})();
