using System.ComponentModel;
using System.Runtime.CompilerServices;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.ViewModels
{
    public class ProductFilterModel : INotifyPropertyChanged
    {
        private CategoryDTO _selectedCategory;
        private CategoryDTO _selectedPlantsHardscape;
        private CategoryDTO _selectedLocalImported;
        private CategoryDTO _selectedIndoorOutdoor;
        private CategoryDTO _selectedPlantFamily;
        private CategoryDTO _selectedDetail;

        public CategoryDTO SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged();
            }
        }

        public CategoryDTO SelectedPlantsHardscape
        {
            get => _selectedPlantsHardscape;
            set
            {
                _selectedPlantsHardscape = value;
                OnPropertyChanged();
            }
        }

        public CategoryDTO SelectedLocalImported
        {
            get => _selectedLocalImported;
            set
            {
                _selectedLocalImported = value;
                OnPropertyChanged();
            }
        }

        public CategoryDTO SelectedIndoorOutdoor
        {
            get => _selectedIndoorOutdoor;
            set
            {
                _selectedIndoorOutdoor = value;
                OnPropertyChanged();
            }
        }

        public CategoryDTO SelectedPlantFamily
        {
            get => _selectedPlantFamily;
            set
            {
                _selectedPlantFamily = value;
                OnPropertyChanged();
            }
        }

        public CategoryDTO SelectedDetail
        {
            get => _selectedDetail;
            set
            {
                _selectedDetail = value;
                OnPropertyChanged();
            }
        }

        public bool HasAnyFilter()
        {
            return SelectedCategory?.CategoryId > 0 ||
                   SelectedPlantsHardscape?.CategoryId > 0 ||
                   SelectedLocalImported?.CategoryId > 0 ||
                   SelectedIndoorOutdoor?.CategoryId > 0 ||
                   SelectedPlantFamily?.CategoryId > 0 ||
                   SelectedDetail?.CategoryId > 0;
        }

        public void ClearAll()
        {
            SelectedCategory = null;
            SelectedPlantsHardscape = null;
            SelectedLocalImported = null;
            SelectedIndoorOutdoor = null;
            SelectedPlantFamily = null;
            SelectedDetail = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}